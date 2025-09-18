using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;

namespace yahd2mm;

struct ModJson {
  public bool Enabled;
  public string Version;
}

struct FileAssociation {
  public int PatchNumber;
  public string AssociatedMod;
  public string[] Files;
}

class ManifestChoices {
  public string Name;
  public string Description;
  public string IconPath;
  public bool IsString;
  public bool Chosen;
  public ManifestChoices[]? SubChoices;
}

class AliasDictionary(ModManager manager)
{
  private ModManager manager = manager;
  public string this[string inp] {
    get {
      if (manager.aliases.TryGetValue(inp, out string? value)) {
        return value;
      }
      return manager.mods.First((v) => v.Guid == inp).Name;
    }
    set {
      manager.aliases[inp] = value;
      manager.SaveData();
    }
  }
}

partial class ModManager {
  internal List<ArsenalMod> mods = [];
  internal EventHandler<ArsenalMod> modAdded = (_, __) => { };
  internal static readonly string ModHolder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "mods");
  static readonly string Record = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "record.json");
  static readonly string State = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "state.json");
  static readonly string Choices = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "choices.json");
  static readonly string Aliases = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "aliases.json");
  static readonly string Favourites = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "favourites.json");
  static readonly string PriorityList = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "priority-list.json");
  internal Dictionary<string, ModJson> modState = File.Exists(State) ? JsonConvert.DeserializeObject<Dictionary<string, ModJson>>(File.ReadAllText(State)) ?? [] : [];
  readonly Dictionary<string, FileAssociation[]> fileRecords = File.Exists(Record) ? JsonConvert.DeserializeObject<Dictionary<string, FileAssociation[]>>(File.ReadAllText(Record)) ?? [] : [];
  internal readonly Dictionary<string, string[]> modChoices = File.Exists(Choices) ? JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(Choices)) ?? [] : [];
  internal Dictionary<string, string> aliases = File.Exists(Aliases) ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Aliases)) ?? [] : [];
  internal List<string> favourites = File.Exists(Favourites) ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Favourites)) ?? [] : [];
  internal string[] priorities = File.Exists(PriorityList) ? JsonConvert.DeserializeObject<string[]>(File.ReadAllText(PriorityList)) ?? [] : [];
  internal AliasDictionary modAliases;
  internal Dictionary<string, ManifestChoices[]> processedChoices = [];

  public ModManager() {
    ProcessMods();
    modAliases = new(this);
  }

  public void SaveData() {
    File.WriteAllText(State, JsonConvert.SerializeObject(modState));
    File.WriteAllText(Record, JsonConvert.SerializeObject(fileRecords));
    Dictionary<string, string[]> proc = [];
    foreach (KeyValuePair<string, ManifestChoices[]> paths in processedChoices) {
      proc[paths.Key] = ChoicesToPaths(paths.Value);
    }
    File.WriteAllText(Choices, JsonConvert.SerializeObject(proc));
    File.WriteAllText(Aliases, JsonConvert.SerializeObject(aliases));
    File.WriteAllText(Favourites, JsonConvert.SerializeObject(favourites));
    File.WriteAllText(PriorityList, JsonConvert.SerializeObject(priorities));
  }

  internal static string[] ChoicesToPaths(ManifestChoices[] choices, string currentPath = "") {
    List<string> result = [];
    foreach (ManifestChoices choice in choices) {
      if (choice.Chosen) {
        result.Add(currentPath + choice.Name);
      }
      if (choice.SubChoices != null) {
        result.AddRange(ChoicesToPaths(choice.SubChoices, currentPath + choice.Name + "/"));
      }
    }
    return [.. result];
  }

  internal void ApplyPriorities() {
    string[] modList = mods.Where((v) => modState[v.Guid].Enabled).OrderBy((v) => Array.FindIndex(priorities, b => b == v.Guid)).Select((v) => v.Guid).ToArray();
    foreach (string mod in modList.Reverse())
      DisableMod(mod);
    foreach (string mod in modList)
      EnableMod(mod);
  }

  public void ActivateAllOptions(string mod) {
    bool reactivate = modState[mod].Enabled;
    if (reactivate) DisableMod(mod);
    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices) {
      choice.Chosen = true;
      foreach (ManifestChoices c in choice.SubChoices ?? []) {
        c.Chosen = true;
      }
    }
    if (reactivate) EnableMod(mod);
    SaveData();
  }

  public void DisableAllOptions(string mod) {
    bool reactivate = modState[mod].Enabled;
    if (reactivate) DisableMod(mod);
    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices) {
      choice.Chosen = false;
      foreach (ManifestChoices c in choice.SubChoices ?? []) {
        c.Chosen = false;
      }
    }
    if (reactivate) EnableMod(mod);
    SaveData();
  }

  public void EnableAllSubOptions(string mod) {
    bool reactivate = modState[mod].Enabled;
    if (reactivate) DisableMod(mod);
    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices) {
      foreach (ManifestChoices c in choice.SubChoices ?? []) {
        c.Chosen = true;
      }
    }
    if (reactivate) EnableMod(mod);
    SaveData();
  }

  public void DisableAllSubOptions(string mod) {
    bool reactivate = modState[mod].Enabled;
    if (reactivate) DisableMod(mod);
    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices) {
      foreach (ManifestChoices c in choice.SubChoices ?? []) {
        c.Chosen = false;
      }
    }
    if (reactivate) EnableMod(mod);
    SaveData();
  }

  public void EnableChoice(string mod, string path) {
    bool reactivate = modState[mod].Enabled;
    if (reactivate) DisableMod(mod);
    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices) {
      if (choice.Name == path) {
        choice.Chosen = true;
      }
      else if (choice.SubChoices != null) {
        string currentPath = choice.Name;
        foreach (ManifestChoices c in choice.SubChoices) {
          EnableChoice(c, path, currentPath);
        }
      }
    }
    if (reactivate) EnableMod(mod);
    SaveData();
  }

  private static void EnableChoice(ManifestChoices choice, string path, string currentPath) {
    if (currentPath + "/" + choice.Name == path) {
      choice.Chosen = true;
    }
    else if (choice.SubChoices != null) {
      string newPath = currentPath + "/" + choice.Name;
      foreach (ManifestChoices c in choice.SubChoices) {
        EnableChoice(c, path, newPath);
      }
    }
  }

  public void DisableChoice(string mod, string path) {
    bool reactivate = modState[mod].Enabled;
    if (reactivate) DisableMod(mod);
    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices) {
      if (choice.Name == path) {
        choice.Chosen = false;
      }
      else if (choice.SubChoices != null) {
        string currentPath = choice.Name;
        foreach (ManifestChoices c in choice.SubChoices) {
          DisableChoice(c, path, currentPath);
        }
      }
    }
    if (reactivate) EnableMod(mod);
    SaveData();
  }

  private static void DisableChoice(ManifestChoices choice, string path, string currentPath) {
    if (currentPath + "/" + choice.Name == path) {
      choice.Chosen = false;
    }
    else if (choice.SubChoices != null) {
      string newPath = currentPath + "/" + choice.Name;
      foreach (ManifestChoices c in choice.SubChoices) {
        EnableChoice(c, path, newPath);
      }
    }
  }

  private static string[][] ProcessFilenamesIntoPatchSets(List<string> filenames) {
    Dictionary<string, (string patch, string gpu, string stream)> patchSets = [];
    foreach (string file in filenames) {
      string name = Path.GetFileName(file);
      Match m = PatchInfoRegex.Match(name);
      if (!m.Success) continue;
      string baseN = m.Groups["base"].Value;
      string index = m.Groups["index"].Value;
      string ext = m.Groups["ext"].Value;
      string dir = new FileInfo(file).Directory!.Name;
      string key = $"{dir}|{baseN}|{index}";
      if (!patchSets.TryGetValue(key, out (string patch, string gpu, string stream) value)) {
        value = (null, null, null);
        patchSets[key] = value;
      }
      var (patch, gpu, stream) = value;
      if (string.IsNullOrEmpty(ext)) {
        patch = file;
      }
      else if (ext == "stream") {
        stream = file;
      }
      else if (ext == "gpu_resources") {
        gpu = file;
      }
      patchSets[key] = (patch, gpu, stream);
    }

    string[][] result = patchSets.OrderBy(p => int.TryParse(p.Key.Split('|')[2], out int idx) ? idx : int.MaxValue).Select(p => {
      var (patch, gpu, stream) = p.Value;
      return new List<string>
      {
        patch,
        stream,
        gpu
      }.Where(f => !string.IsNullOrEmpty(f)).ToArray();
    }).ToArray();
    return result;
  }

  private string[][] ProcessChoicesIntoPatchSets(ArsenalMod mod) {
    string BasePath = Path.Join(ModHolder, mod.FolderName);
    ArsenalManifest manifest = mod.Manifest!.Value;
    List<string> filenames = [];
    if (manifest.Options != null && manifest.Options.Length > 0)
    {
      ManifestChoices[] choices = processedChoices[mod.Guid];
      string[] Choices = CoalesceSubChoices("", choices);
      string[] includes = ProcessChoices(mod, Choices, manifest.Options!);
      foreach (string include in includes)
      {
        string path = Path.Join(BasePath, include);
        if (File.Exists(path))
        {
          filenames.Add(path);
        }
        else if (Directory.Exists(path))
        {
          filenames.AddRange(GetFolderRecursively(path).Where((v) => FileNumRegex.Match(v).Success));
        }
      }
    }
    else {
      string[] files = GetFolderRecursively(BasePath);
      foreach (string file in files.Where((v) => FileNumRegex.Match(v).Success)) {
        if (FileNumRegex.Match(file).Success) {
          filenames.Add(file);
        }
      }
    }
    return ProcessFilenamesIntoPatchSets(filenames);
  }

  private static string[] GetFolderRecursively(string path) {
    List<string> files = [];
    foreach (string file in Directory.EnumerateFiles(path)) {
      files.Add(file);
    }
    foreach (string directory in Directory.EnumerateDirectories(path)) {
      files.AddRange(GetFolderRecursively(directory));
    }
    return [.. files];
  }

  private static string[] SubChoicesToIncludes(object[] options) {
    List<string> files = [];
    foreach (object opt in options) {
      if (opt is JObject jobj)
      {
        ArsenalOption option = jobj.ToObject<ArsenalOption>();
        files.AddRange(option.Include!);
      }
      else if (opt is string opti) {
        files.Add(opti);
      }
    }
    return files.Where((v) => FileNumRegex.Match(v).Success).ToArray();
  }

  private static bool IsIncludeRedundant(ArsenalMod mod, string include, List<string> encountered) {
    string path = Path.Join(ModHolder, mod.FolderName, include);
    string[] FileAndDirectoryNames = [.. Directory.EnumerateFiles(path).Select((v) => new FileInfo(v).Name), .. Directory.EnumerateDirectories(path).Select((v) => new DirectoryInfo(v).Name)];
    return encountered.All(FileAndDirectoryNames.Contains);
  }

  private static string[] ProcessChoices(ArsenalMod mod, string[] chosen, object[] options) {
    List<string> files = [];
    List<string> EncounteredIncludes = [];
    foreach (object opt in options) {
      if (opt is JObject jobj)
      {
        ArsenalOption option = jobj.ToObject<ArsenalOption>();
        if (chosen.Contains(option.Name))
        {
          if (option.SubOptions != null)
          {
            EncounteredIncludes.AddRange(SubChoicesToIncludes(option.SubOptions));
            files.AddRange(ProcessSubChoices(chosen, option.Name, option.SubOptions));
          }
          if (option.Include != null && option.SubOptions != null)
          {
            foreach (string include in option.Include) {
              bool redundant = IsIncludeRedundant(mod, include, EncounteredIncludes);
              if (!redundant) {
                files.Add(include);
              }
            }
          }
          else if (option.Include != null) {
            files.AddRange(option.Include);
          }
        }
      }
      else if (opt is string opti) {
        if (chosen.Contains(opti)) {
          files.Add(opti);
        }
      }
    }
    return [..files];
  }

  private static string[] ProcessSubChoices(string[] chosen, string currentPath, object[] options) {
    List<string> files = [];
    foreach (object opt in options) {
      if (opt is JObject jobj)
      {
        ArsenalOption option = jobj.ToObject<ArsenalOption>();
        if (chosen.Contains(currentPath + "/" + option.Name))
        {
          files.AddRange(option.Include!);
        }
      }
      else if (opt is string opti) {
        if (chosen.Contains(currentPath + "/" + opti)) {
          files.Add(opti);
        }
      }
    }
    return [..files];
  }

  private static string[] CoalesceSubChoices(string baseName, ManifestChoices[] subChoices) {
    List<string> Choices = [];
    foreach (ManifestChoices choice in subChoices) {
      if (choice.Chosen) {
        string combined = baseName != "" ? baseName + "/" + choice.Name : choice.Name;
        Choices.Add(combined);
        if (choice.SubChoices != null) {
          Choices.AddRange(CoalesceSubChoices(combined, choice.SubChoices));
        }
      }
    }
    return [.. Choices];
  }

  internal static readonly Regex FileNumRegex = IsPatch();
  internal static readonly Regex PatchAtEndRegex = IsPatchFile();
  internal static readonly Regex GPUResourcesRegex = IsGPUResources();
  internal static readonly Regex StreamRegex = IsStream();
  internal static readonly Regex PatchInfoRegex = GrabPatchInfo();
  
  private static string[] GetFirstValidPatchSet(string[] set, int patchNum, List<string> existing) {
    string[] sset = set.Select((v) => Path.Join(EntryPoint.HD2Path, new FileInfo(v).Name)).ToArray();
    string[] extraChecks = [];
    if (!sset.Any((v) => PatchAtEndRegex.Match(v).Success)) {
      string name = sset.First();
      string basename = name[..name.IndexOf('.')];
      extraChecks = [ .. extraChecks, basename + ".patch_" + patchNum.ToString()];
    }
    if (!sset.Any(GPUResourcesRegex.IsMatch)) {
      string name = sset.FirstOrDefault((v) => PatchAtEndRegex.Match(v!).Success, null) ?? extraChecks.First((v) => PatchAtEndRegex.Match(v).Success);
      extraChecks = [.. extraChecks, name + ".gpu_resources"];
    }
    if (!sset.Any(StreamRegex.IsMatch)) {
      string name = sset.FirstOrDefault((v) => PatchAtEndRegex.Match(v!).Success, null) ?? extraChecks.First((v) => PatchAtEndRegex.Match(v).Success);
      extraChecks = [.. extraChecks, name + ".stream"];
    }
    int currentPatch = patchNum - 1;
    while (sset.Any(existing.Contains) || extraChecks.Any(existing.Contains)) {
      sset = sset.Select((v) => FileNumRegex.Replace(v, (currentPatch + 1).ToString())).ToArray();
      extraChecks = extraChecks.Select((v) => FileNumRegex.Replace(v, (currentPatch + 1).ToString())).ToArray();
      currentPatch++;
    }
    return sset;
  }

  public void EnableMod(string name) {
    ModJson state = modState[name];
    if (state.Enabled) return;
    ArsenalMod mod = mods.First((m) => m.Guid == name);
    string[][] filenames;
    if (mod.Manifest.HasValue) {
      filenames = ProcessChoicesIntoPatchSets(mod);
    }
    else {
      filenames = ProcessFilenamesIntoPatchSets([.. mod.Files!]);
    }
    Dictionary<string[], string> basenames = [];
    foreach (string[] set in filenames) {
      string first = set.First();
      string fname = new FileInfo(first).Name;
      string withoutExt = fname.Contains('.') ? fname[..fname.IndexOf('.')] : fname;
      basenames[set] = withoutExt;
    }
    Dictionary<string, string[][]> nextAssoc = [];
    foreach (string[] set in filenames) {
      string basename = basenames[set];
      if (nextAssoc.TryGetValue(basename, out string[][] tsets))
      {
        tsets = [.. tsets, set];
        nextAssoc[basename] = tsets;
      }
      else {
        nextAssoc[basename] = [set];
      }
    }
    foreach (KeyValuePair<string, string[][]> pair in nextAssoc) {
      List<string> existingPatches = Directory.EnumerateFiles(EntryPoint.HD2Path).Where((v) => FileNumRegex.Match(v).Success).ToList();
      if (fileRecords.TryGetValue(pair.Key, out FileAssociation[]? existing))
      {
        if (existing.Where((v) => v.AssociatedMod == name).FirstOrDefault(new FileAssociation() { AssociatedMod = "NoValidModFoundOrDefault" }).AssociatedMod != "NoValidModFoundOrDefault")
        {
          continue;
        }
        FileAssociation assoc = new()
        {
          AssociatedMod = name,
          Files = [..pair.Value.SelectMany((v, index) => {
            string[] set = GetFirstValidPatchSet(v, index, existingPatches);
            existingPatches.AddRange(set);
            for (int i = 0; i < v.Length; i++) {
              EntryPoint.queue.CreateSymbolicLink(v[i], set[i]);
            }
            return set;
          })]
        };
        existing = [.. existing, assoc with { PatchNumber = existing.Length }];
        fileRecords[pair.Key] = existing;
      }
      else {
        FileAssociation assoc = new()
        {
          AssociatedMod = name,
          Files = [..pair.Value.SelectMany((v, index) => {
            string[] set = GetFirstValidPatchSet(v, index, existingPatches);
            existingPatches.AddRange(set);
            for (int i = 0; i < v.Length; i++) {
              EntryPoint.queue.CreateSymbolicLink(v[i], set[i]);
            }
            return set;
          })],
          PatchNumber = 0
        };
        fileRecords[pair.Key] = [assoc];
      }
    }
    modState[name] = state with { Enabled = true };
    EntryPoint.queue.WaitForEmpty();
    SaveData();
  }

  public void DisableMod(string name) {
    ModJson state = modState[name];
    if (!state.Enabled) return;
    ArsenalMod mod = mods.First((m) => m.Guid == name);
    string[][] filenames;
    if (mod.Manifest.HasValue) {
      filenames = ProcessChoicesIntoPatchSets(mod);
    }
    else {
      filenames = ProcessFilenamesIntoPatchSets([..mod.Files!]);
    }
    Dictionary<string[], string> basenames = [];
    foreach (string[] set in filenames) {
      string fset = set.First();
      string fname = new FileInfo(fset).Name;
      string withoutExt = fname.Contains('.') ? fname[..fname.IndexOf('.')] : fname;
      basenames[set] = withoutExt;
    }
    Dictionary<string, FileAssociation[]> toDowngrade = [];
    Dictionary<string, HashSet<string>> deleting = [];
    foreach (string[] set in filenames) {
      string basename = basenames[set];
      FileAssociation[] associations = fileRecords.TryGetValue(basename, out FileAssociation[]? value) ? value : [];
      FileAssociation? ourAssociation = null;
      foreach (FileAssociation assoc in associations) {
        if (assoc.AssociatedMod == name) {
          ourAssociation = assoc;
          break;
        }
      }
      if (ourAssociation.HasValue) {
        FileAssociation[] downgrading = associations.Where((v) => v.PatchNumber > ourAssociation.Value.PatchNumber).ToArray();
        toDowngrade[basename] = downgrading;
        foreach (string file in ourAssociation.Value.Files) {
          if (deleting.TryGetValue(basename, out HashSet<string>? v)) {
            v.Add(file);
          }
          else {
            deleting[basename] = [file];
          }
        }
      }
    }
    foreach (KeyValuePair<string[], string> basename in basenames) {
      foreach (string file in deleting[basename.Value]) {
        if (File.Exists(file))
          EntryPoint.queue.Delete(file);
      }
    }
    EntryPoint.queue.WaitForEmpty();
    foreach (KeyValuePair<string, FileAssociation[]> downgrading in toDowngrade) {
      if (downgrading.Value.Length == 0 && fileRecords[downgrading.Key].Length == 1)  {
        fileRecords.Remove(downgrading.Key);
        continue;
      };
      if (downgrading.Value.Length == 0 && fileRecords[downgrading.Key].Length == 0) {
        fileRecords.Remove(downgrading.Key);
        continue;
      };
      if (downgrading.Value.Length == 0)
      {
        fileRecords[downgrading.Key] = fileRecords[downgrading.Key].Where((v) => v.AssociatedMod != mod.Guid).ToArray();
        continue;
      }
      int lowestPatchNumberPair = downgrading.Value.OrderBy((v) => v.PatchNumber).First().PatchNumber;
      FileAssociation[] withoutDowngraded = fileRecords[downgrading.Key].Where((v) => v.PatchNumber < lowestPatchNumberPair).ToArray();
      int highestPatchNumber = -1;
      if (withoutDowngraded.Length > 0) {
        foreach (FileAssociation notDowngraded in withoutDowngraded.OrderBy((v) => v.PatchNumber)) {
          if (Math.Abs(notDowngraded.PatchNumber - highestPatchNumber) == 1) {
            highestPatchNumber = notDowngraded.PatchNumber;
          }
        }
      }
      FileAssociation[] downgraded = [];
      List<string> existingPatches = Directory.EnumerateFiles(EntryPoint.HD2Path).Where((v) => FileNumRegex.Match(v).Success).ToList();
      foreach (FileAssociation association in downgrading.Value.OrderBy((v) => v.PatchNumber)) {
        FileAssociation baseMod = association with { PatchNumber = highestPatchNumber + 1 };
        string[] newFiles = [];
        foreach (string file in association.Files)
        {
          EntryPoint.queue.Delete(file);
          existingPatches.Remove(file);
        }
        ArsenalMod m = mods.First((v) => v.Guid == association.AssociatedMod);
        string[][] sets;
        if (m.Manifest.HasValue) {
          sets = ProcessChoicesIntoPatchSets(m);
        }
        else {
          sets = ProcessFilenamesIntoPatchSets([.. m.Files!]);
        }
        newFiles = [..sets.SelectMany((v, index) => {
          string[] set = GetFirstValidPatchSet(v, index, existingPatches);
          existingPatches.AddRange(set);
          for (int i = 0; i < v.Length; i++) {
            EntryPoint.queue.CreateSymbolicLink(v[i], set[i]);
          }
          return set;
        })];
        baseMod.Files = newFiles;
        downgraded = [.. downgraded, baseMod];
        highestPatchNumber++;
      }
      FileAssociation[] combined = [.. withoutDowngraded, .. downgraded];
      fileRecords[downgrading.Key] = combined.Where((v) => v.AssociatedMod != mod.Guid).ToArray();
    }
    modState[name] = state with { Enabled = false };
    EntryPoint.queue.WaitForEmpty();
    SaveData();
  }

  public void UninstallMod(string name) {
    DisableMod(name);
    modState.Remove(name);
    ArsenalMod mod = mods.First((m) => m.Guid == name);
    string ModDirectoryPath = Path.Join(ModHolder, mod.FolderName);
    Directory.Delete(ModDirectoryPath, true);
    mods.Remove(mod);
    modChoices.Remove(name);
    aliases.Remove(name);
    priorities = priorities.Where(v => v != name).ToArray();
    SaveData();
  }

  private void ProcessMods() {
    if (!Directory.Exists(ModHolder)) {
      Directory.CreateDirectory(ModHolder);
      return;
    }
    string[] directories = Directory.EnumerateDirectories(ModHolder).ToArray();
    foreach (string dir in directories)
      ProcessMod(dir);
    ArsenalMod[] arr = [.. mods];
    Array.Sort(arr, static (x, y) => string.Compare(x.Name, y.Name));
    mods = [.. arr];
  }

  internal ArsenalMod ProcessMod(string path) {
    bool hasManifest = File.Exists(Path.Join(path, "manifest.json"));
    if (hasManifest) {
      ArsenalMod mod = ModWithManifest(path);
      mods = mods.Where((v) => v.Guid != mod.Guid).ToList();
      mods.Add(mod);
      modAdded.Invoke(null, mod);
      if (!priorities.Contains(mod.Guid)) {
        priorities = [.. priorities, mod.Guid];
      }
      return mod;
    }
    else {
      ArsenalMod mod = ModWithoutManifest(path);
      mods = mods.Where((v) => v.Guid != mod.Guid).ToList();
      mods.Add(mod);
      modAdded.Invoke(null, mod);
      if (!priorities.Contains(mod.Guid)) {
        priorities = [.. priorities, mod.Guid];
      }
      return mod;
    }
  }

  private static string GetName(string path) {
    if (File.Exists(path))
    {
      return new FileInfo(path).Directory?.Name ?? string.Empty;
    }
    else if (Directory.Exists(path))
    {
      return new DirectoryInfo(path).Name;
    }
    return string.Empty;
  }
  
  private static ManifestChoices[] GenerateDefaultManifestChoices(ArsenalManifest mod) {
    List<ManifestChoices> choices = [];
    if (mod.Options == null || mod.Options.Length == 0) return [];
    foreach (object opt in mod.Options) {
      if (opt is JObject jobj)
      {
        ArsenalOption option = jobj.ToObject<ArsenalOption>();
        ManifestChoices choice = new()
        {
          Name = option.Name,
          IconPath = option.Image,
          Description = option.Description,
          IsString = false,
          Chosen = false
        };
        if (option.SubOptions != null)
        {
          choice.SubChoices = GetSubChoices(option.SubOptions);
        }
        choices.Add(choice);
      }
      else if (opt is string opti) {
        ManifestChoices choice = new()
        {
          Name = opti,
          IsString = true,
          Chosen = false
        };
        choices.Add(choice);
      }
    }
    return [.. choices];
  }

  private static void ApplyChoices(ManifestChoices[] choices, string[] paths) {
    foreach (ManifestChoices choice in choices) {
      if (paths.Contains(choice.Name)) {
        choice.Chosen = true;
      }
      if (choice.SubChoices != null) {
        ApplySubChoices(choice.SubChoices, paths, choice.Name + "/");
      }
    }
  }

  private static void ApplySubChoices(ManifestChoices[] choices, string[] paths, string currentPath) {
    foreach (ManifestChoices choice in choices) {
      if (paths.Contains(currentPath + choice.Name)) {
        choice.Chosen = true;
      }
    }
  }

  private static ManifestChoices[] GetSubChoices(object[] options) {
    List<ManifestChoices> choices = [];
    foreach (object opt in options) {
      if (opt is JObject jobj)
      {
        ArsenalSubOption option = jobj.ToObject<ArsenalSubOption>();
        ManifestChoices choice = new()
        {
          Chosen = false,
          Description = option.Description,
          IconPath = option.Image,
          Name = option.Name,
          IsString = false
        };
        choices.Add(choice);
      }
      else if (opt is string opti) {
        ManifestChoices choice = new()
        {
          Chosen = false,
          Name = opti,
          IsString = true
        };
        choices.Add(choice);
      }
    }
    return [.. choices];
  }

  private ArsenalMod ModWithManifest(string path) {
    ArsenalManifest manifest = JsonConvert.DeserializeObject<ArsenalManifest>(File.ReadAllText(Path.Join(path, "manifest.json")));
    if (!modState.TryGetValue(manifest.Guid, out ModJson state))
    {
      state = new()
      {
        Version = "1.0.0",
        Enabled = false
      };
      modState[manifest.Guid] = state;
    };
    string[]? files = null;
    if (!processedChoices.ContainsKey(manifest.Guid)) {
      ManifestChoices[] choices = GenerateDefaultManifestChoices(manifest);
      if (choices.Length > 0)
      {
        if (modChoices.TryGetValue(manifest.Guid, out string[]? value))
          ApplyChoices(choices, value);
        processedChoices[manifest.Guid] = choices;
      }
      else {
        files = GetFolderRecursively(path).Where((v) => FileNumRegex.Match(v).Success).ToArray();
      }
    }
    ArsenalMod mod = new()
    {
      Manifest = manifest,
      Version = SemVersion.Parse(state.Version, SemVersionStyles.Any),
      Name = manifest.Name,
      FolderName = GetName(path),
      Files = files
    };
    return mod;
  }

  private ArsenalMod ModWithoutManifest(string path) {
    string name = GetName(path);
    if (!modState.TryGetValue(name, out ModJson state))
    {
      state = new()
      {
        Version = "1.0.0",
        Enabled = false
      };
      modState[name] = state;
    };
    ArsenalMod mod = new()
    {
      Version = SemVersion.Parse(state.Version, SemVersionStyles.Any),
      Files = Directory.EnumerateFiles(path).Where((v) => FileNumRegex.Match(v).Success).ToArray(),
      Name = name,
      FolderName = name
    };
    return mod;
  }

  [GeneratedRegex(@"(?<=patch_)\d+")]
  private static partial Regex IsPatch();
  [GeneratedRegex(@"\.patch_(\d+)$")]
  private static partial Regex IsPatchFile();
  [GeneratedRegex(@"\.patch_(\d+)\.gpu_resources$")]
  private static partial Regex IsGPUResources();
  [GeneratedRegex(@"\.patch_(\d+)\.stream$")]
  private static partial Regex IsStream();

  [GeneratedRegex(@"^(?<base>[^\.]+)\.patch_(?<index>\d+)(?:\.(?<ext>stream|gpu_resources))?$")]
  private static partial Regex GrabPatchInfo();
}
