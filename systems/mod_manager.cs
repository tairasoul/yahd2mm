using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;

namespace yahd2mm;

struct ModJson
{
  public bool Enabled;
  public string Version;
  public long InstalledAt;
}

struct FileAssociation
{
  public string AssociatedMod;
  public string[] Files;
}

class ManifestChoices
{
  public string? Name;
  public string? Description;
  public string? IconPath;
  public bool IsString;
  public bool Chosen;
  public ManifestChoices[]? SubChoices;
}

class AliasDictionary(ModManager manager)
{
  private readonly ModManager manager = manager;
  public string this[string inp]
  {
    get
    {
      return manager.aliases.TryGetValue(inp, out string? value) ? value : manager.mods.First((v) => v.Guid == inp).Name;
    }
    set
    {
      manager.aliases[inp] = value;
      manager.SaveData();
    }
  }
}

partial class ModManager
{
  internal List<ArsenalMod> mods = [];
  internal EventHandler<ArsenalMod> modAdded = static (_, __) => { };
  internal static readonly string yahd2mm_basepath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm");
  internal static readonly string ModHolder = Path.Join(yahd2mm_basepath, "mods");
  static readonly string Record = Path.Join(yahd2mm_basepath, "record.json");
  static readonly string State = Path.Join(yahd2mm_basepath, "state.json");
  static readonly string Choices = Path.Join(yahd2mm_basepath, "choices.json");
  static readonly string Aliases = Path.Join(yahd2mm_basepath, "aliases.json");
  static readonly string Favourites = Path.Join(yahd2mm_basepath, "favourites.json");
  static readonly string PriorityList = Path.Join(yahd2mm_basepath, "priority-list.json");
  internal Dictionary<string, ModJson> modState = File.Exists(State) ? JsonConvert.DeserializeObject<Dictionary<string, ModJson>>(File.ReadAllText(State)) ?? [] : [];
  internal readonly Dictionary<string, FileAssociation[]> fileRecords = File.Exists(Record) ? JsonConvert.DeserializeObject<Dictionary<string, FileAssociation[]>>(File.ReadAllText(Record)) ?? [] : [];
  internal readonly Dictionary<string, string[]> modChoices = File.Exists(Choices) ? JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(Choices)) ?? [] : [];
  internal Dictionary<string, string> aliases = File.Exists(Aliases) ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Aliases)) ?? [] : [];
  internal List<string> favourites = File.Exists(Favourites) ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Favourites)) ?? [] : [];
  internal string[] priorities = File.Exists(PriorityList) ? JsonConvert.DeserializeObject<string[]>(File.ReadAllText(PriorityList)) ?? [] : [];
  internal AliasDictionary modAliases;
  internal Dictionary<string, ManifestChoices[]> processedChoices = [];
  private static readonly List<string> existing = [];

  private void SetupExistingList() {
    foreach (FileAssociation association in fileRecords.Values.SelectMany((v) => v)) {
      existing.AddRange(association.Files);
    }
  }

  public ModManager()
  {
    ProcessMods();
    modAliases = new(this);
    priorities = [.. priorities.Where((v) => mods.Any((b) => b.Guid == v))];
    SetupExistingList();
  }

  public void SaveData()
  {
    File.WriteAllText(State, JsonConvert.SerializeObject(modState));
    File.WriteAllText(Record, JsonConvert.SerializeObject(fileRecords));
    Dictionary<string, string[]> proc = [];
    foreach (KeyValuePair<string, ManifestChoices[]> paths in processedChoices)
    {
      proc[paths.Key] = ChoicesToPaths(paths.Value);
    }
    File.WriteAllText(Choices, JsonConvert.SerializeObject(proc));
    File.WriteAllText(Aliases, JsonConvert.SerializeObject(aliases));
    File.WriteAllText(Favourites, JsonConvert.SerializeObject(favourites));
    File.WriteAllText(PriorityList, JsonConvert.SerializeObject(priorities));
  }

  internal static string[] ChoicesToPaths(ManifestChoices[] choices, string currentPath = "")
  {
    List<string> result = [];
    foreach (ManifestChoices choice in choices)
    {
      if (choice.Chosen)
      {
        result.Add(currentPath + choice.Name);
      }
      if (choice.SubChoices != null)
      {
        result.AddRange(ChoicesToPaths(choice.SubChoices, currentPath + choice.Name + "/"));
      }
    }
    return [.. result];
  }

  internal void ApplyPriorities()
  {
    string[] modList = [.. mods.Where((v) => modState[v.Guid].Enabled).OrderBy((v) => Array.FindIndex(priorities, b => b == v.Guid)).Select((v) => v.Guid)];
    foreach (string mod in modList.Reverse())
    {
      DisableMod(mod);
    }

    foreach (string mod in modList)
    {
      EnableMod(mod);
    }
  }

  public void ActivateAllOptions(string mod)
  {
    if (!processedChoices.TryGetValue(mod, out ManifestChoices[]? choices))
    {
      return;
    }

    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    foreach (ManifestChoices choice in choices)
    {
      choice.Chosen = true;
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  public void DisableAllOptions(string mod)
  {
    if (!processedChoices.TryGetValue(mod, out ManifestChoices[]? choices))
    {
      return;
    }

    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    foreach (ManifestChoices choice in choices)
    {
      choice.Chosen = false;
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  public void ActivateAllOptionsAndSubOptions(string mod)
  {
    if (!processedChoices.TryGetValue(mod, out ManifestChoices[]? choices))
    {
      return;
    }

    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    foreach (ManifestChoices choice in choices)
    {
      choice.Chosen = true;
      foreach (ManifestChoices c in choice.SubChoices ?? [])
      {
        c.Chosen = true;
      }
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  public void DisableAllOptionsAndSubOptions(string mod)
  {
    if (!processedChoices.TryGetValue(mod, out ManifestChoices[]? choices))
    {
      return;
    }

    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    foreach (ManifestChoices choice in choices)
    {
      choice.Chosen = false;
      foreach (ManifestChoices c in choice.SubChoices ?? [])
      {
        c.Chosen = false;
      }
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  public void EnableAllSubOptions(string mod)
  {
    if (!processedChoices.TryGetValue(mod, out ManifestChoices[]? choices))
    {
      return;
    }

    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    foreach (ManifestChoices choice in choices)
    {
      foreach (ManifestChoices c in choice.SubChoices ?? [])
      {
        c.Chosen = true;
      }
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  public void DisableAllSubOptions(string mod)
  {
    if (!processedChoices.TryGetValue(mod, out ManifestChoices[]? choices))
    {
      return;
    }

    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    foreach (ManifestChoices choice in choices)
    {
      foreach (ManifestChoices c in choice.SubChoices ?? [])
      {
        c.Chosen = false;
      }
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  public void EnableChoice(string mod, string path)
  {
    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices)
    {
      if (choice.Name == path)
      {
        choice.Chosen = true;
      }
      else if (choice.SubChoices != null)
      {
        string currentPath = choice.Name;
        foreach (ManifestChoices c in choice.SubChoices)
        {
          EnableChoice(c, path, currentPath);
        }
      }
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  private static void EnableChoice(ManifestChoices choice, string path, string currentPath)
  {
    if (currentPath + "/" + choice.Name == path)
    {
      choice.Chosen = true;
    }
    else if (choice.SubChoices != null)
    {
      string newPath = currentPath + "/" + choice.Name;
      foreach (ManifestChoices c in choice.SubChoices)
      {
        EnableChoice(c, path, newPath);
      }
    }
  }

  public void DisableChoice(string mod, string path)
  {
    bool reactivate = modState[mod].Enabled;
    if (reactivate)
    {
      DisableMod(mod);
    }

    ManifestChoices[] choices = processedChoices[mod];
    foreach (ManifestChoices choice in choices)
    {
      if (choice.Name == path)
      {
        choice.Chosen = false;
      }
      else if (choice.SubChoices != null)
      {
        string currentPath = choice.Name;
        foreach (ManifestChoices c in choice.SubChoices)
        {
          DisableChoice(c, path, currentPath);
        }
      }
    }
    if (reactivate)
    {
      EnableMod(mod);
    }

    SaveData();
  }

  private static void DisableChoice(ManifestChoices choice, string path, string currentPath)
  {
    if (currentPath + "/" + choice.Name == path)
    {
      choice.Chosen = false;
    }
    else if (choice.SubChoices != null)
    {
      string newPath = currentPath + "/" + choice.Name;
      foreach (ManifestChoices c in choice.SubChoices)
      {
        EnableChoice(c, path, newPath);
      }
    }
  }

  private static string[][] ProcessFilenamesIntoPatchSets(List<string> filenames)
  {
    Dictionary<string, (string patch, string gpu, string stream)> patchSets = [];
    foreach (string file in filenames)
    {
      string name = Path.GetFileName(file);
      Match m = PatchInfoRegex.Match(name);
      if (!m.Success)
      {
        continue;
      }

      string baseN = m.Groups["base"].Value;
      string index = m.Groups["index"].Value;
      string ext = m.Groups["ext"].Value;
      string dir = new FileInfo(file).Directory!.FullName;
      string key = $"{dir}|{baseN}|{index}";
      if (!patchSets.TryGetValue(key, out (string patch, string gpu, string stream) value))
      {
        value = (null, null, null);
        patchSets[key] = value;
      }
      (string patch, string gpu, string stream) = value;
      if (string.IsNullOrEmpty(ext))
      {
        patch = file;
      }
      else if (ext == "stream")
      {
        stream = file;
      }
      else if (ext == "gpu_resources")
      {
        gpu = file;
      }
      patchSets[key] = (patch, gpu, stream);
    }

    string[][] result = [.. patchSets.OrderBy(static p => int.TryParse(p.Key.Split('|')[2], out int idx) ? idx : int.MaxValue).Select(static p => {
        (string patch, string gpu, string stream) = p.Value;
        return new List<string>
        {
          patch,
          stream,
          gpu
        }.Where(static f => !string.IsNullOrEmpty(f)).ToArray();
      })];
    return result;
  }

  private string[][] ProcessChoicesIntoPatchSets(ArsenalMod mod)
  {
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
          filenames.AddRange(GetFolderRecursively(path).Where(static (v) => FileNumRegex.Match(v).Success));
        }
      }
    }
    else
    {
      string[] files = GetFolderRecursively(BasePath);
      foreach (string file in files.Where(static (v) => FileNumRegex.Match(v).Success))
      {
        if (FileNumRegex.Match(file).Success)
        {
          filenames.Add(file);
        }
      }
    }
    return ProcessFilenamesIntoPatchSets(filenames);
  }

  private static string[] GetFolderRecursively(string path)
  {
    List<string> files = [];
    foreach (string file in Directory.EnumerateFiles(path))
    {
      files.Add(file);
    }
    foreach (string directory in Directory.EnumerateDirectories(path))
    {
      files.AddRange(GetFolderRecursively(directory));
    }
    return [.. files];
  }

  private static string[] SubChoicesToIncludes(object[] options)
  {
    List<string> files = [];
    foreach (object opt in options)
    {
      if (opt is JObject jobj)
      {
        ArsenalOption option = jobj.ToObject<ArsenalOption>();
        foreach (string include in option.Include!)
        {
          if (string.IsNullOrEmpty(include) || string.IsNullOrWhiteSpace(include))
          {
            continue;
          }

          files.Add(include);
        }
      }
      else if (opt is string opti)
      {
        if (string.IsNullOrEmpty(opti) || string.IsNullOrWhiteSpace(opti))
        {
          continue;
        }

        files.Add(opti);
      }
    }
    return [.. files.Where(static (v) => FileNumRegex.Match(v).Success)];
  }

  private static bool IsIncludeRedundant(ArsenalMod mod, string include, List<string> encountered)
  {
    string path = Path.Join(ModHolder, mod.FolderName, include);
    string[] FileAndDirectoryNames = [.. Directory.EnumerateFiles(path).Select(static (v) => new FileInfo(v).Name), .. Directory.EnumerateDirectories(path).Select(static (v) => new DirectoryInfo(v).Name)];
    return encountered.All(FileAndDirectoryNames.Contains);
  }

  private static string[] ProcessChoices(ArsenalMod mod, string[] chosen, object[] options)
  {
    List<string> files = [];
    List<string> EncounteredIncludes = [];
    foreach (object opt in options)
    {
      if (opt is JObject jobj)
      {
        ArsenalOption option = jobj.ToObject<ArsenalOption>();
        if (chosen.Contains(option.Name))
        {
          if (option.SubOptions != null)
          {
            EncounteredIncludes.AddRange(SubChoicesToIncludes(option.SubOptions).Where(static (v) => !string.IsNullOrEmpty(v) && !string.IsNullOrWhiteSpace(v)));
            foreach (string include in ProcessSubChoices(chosen, option.Name, option.SubOptions))
            {
              if (string.IsNullOrEmpty(include) || string.IsNullOrWhiteSpace(include))
              {
                continue;
              }

              files.Add(include);
            }
          }
          if (option.Include != null && option.SubOptions != null)
          {
            foreach (string include in option.Include)
            {
              if (string.IsNullOrEmpty(include) || string.IsNullOrWhiteSpace(include))
              {
                continue;
              }

              bool redundant = IsIncludeRedundant(mod, include, EncounteredIncludes);
              if (!redundant)
              {
                files.Add(include);
              }
            }
          }
          else if (option.Include != null)
          {
            foreach (string include in option.Include)
            {
              if (string.IsNullOrEmpty(include) || string.IsNullOrWhiteSpace(include))
              {
                continue;
              }

              files.Add(include);
            }
          }
        }
      }
      else if (opt is string opti)
      {
        if (string.IsNullOrEmpty(opti) || string.IsNullOrWhiteSpace(opti))
        {
          continue;
        }

        if (chosen.Contains(opti))
        {
          files.Add(opti);
        }
      }
    }
    return [.. files];
  }

  private static string[] ProcessSubChoices(string[] chosen, string currentPath, object[] options)
  {
    List<string> files = [];
    foreach (object opt in options)
    {
      if (opt is JObject jobj)
      {
        ArsenalOption option = jobj.ToObject<ArsenalOption>();
        if (chosen.Contains(currentPath + "/" + option.Name))
        {
          files.AddRange(option.Include!);
        }
      }
      else if (opt is string opti)
      {
        if (chosen.Contains(currentPath + "/" + opti))
        {
          files.Add(opti);
        }
      }
    }
    return [.. files];
  }

  private static string[] CoalesceSubChoices(string baseName, ManifestChoices[] subChoices)
  {
    List<string> Choices = [];
    foreach (ManifestChoices choice in subChoices)
    {
      if (choice.Chosen)
      {
        string combined = baseName != "" ? baseName + "/" + choice.Name : choice.Name;
        Choices.Add(combined);
        if (choice.SubChoices != null)
        {
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

  private static string[] GetFirstValidPatchSet(string[] set, int patchNum)
  {
    string[] sset = [.. set.Select((v) => Path.Join(EntryPoint.HD2Path, new FileInfo(v).Name))];
    string[] extraChecks = [];
    if (!sset.Any((v) => PatchAtEndRegex.Match(v).Success))
    {
      string name = sset.First();
      string basename = name[..name.IndexOf('.')];
      extraChecks = [.. extraChecks, basename + ".patch_" + patchNum.ToString()];
    }
    if (!sset.Any(GPUResourcesRegex.IsMatch))
    {
      string name = sset.FirstOrDefault((v) => PatchAtEndRegex.Match(v!).Success, null) ?? extraChecks.First((v) => PatchAtEndRegex.Match(v).Success);
      extraChecks = [.. extraChecks, name + ".gpu_resources"];
    }
    if (!sset.Any(StreamRegex.IsMatch))
    {
      string name = sset.FirstOrDefault((v) => PatchAtEndRegex.Match(v!).Success, null) ?? extraChecks.First((v) => PatchAtEndRegex.Match(v).Success);
      extraChecks = [.. extraChecks, name + ".stream"];
    }
    int currentPatch = patchNum - 1;
    while (sset.Any(existing.Contains) || extraChecks.Any(existing.Contains))
    {
      sset = [.. sset.Select((v) => FileNumRegex.Replace(v, (currentPatch + 1).ToString()))];
      extraChecks = [.. extraChecks.Select((v) => FileNumRegex.Replace(v, (currentPatch + 1).ToString()))];
      currentPatch++;
    }
    return sset;
  }

  public void EnableMod(string name)
  {
    ModJson state = modState[name];
    if (state.Enabled)
    {
      return;
    }

    ArsenalMod mod = mods.First((m) => m.Guid == name);
    string[][] filenames = mod.Manifest.HasValue ? ProcessChoicesIntoPatchSets(mod) : ProcessFilenamesIntoPatchSets([.. mod.Files!]);
    Dictionary<string[], string> basenames = [];
    foreach (string[] set in filenames)
    {
      string first = set.First();
      string fname = new FileInfo(first).Name;
      string withoutExt = fname.Contains('.') ? fname[..fname.IndexOf('.')] : fname;
      basenames[set] = withoutExt;
    }
    Dictionary<string, string[][]> nextAssoc = [];
    foreach (string[] set in filenames)
    {
      string basename = basenames[set];
      if (nextAssoc.TryGetValue(basename, out string[][] tsets))
      {
        tsets = [.. tsets, set];
        nextAssoc[basename] = tsets;
      }
      else
      {
        nextAssoc[basename] = [set];
      }
    }
    foreach (KeyValuePair<string, string[][]> pair in nextAssoc)
    {
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
              string[] set = GetFirstValidPatchSet(v, index);
              ModManager.existing.AddRange(set);
              for (int i = 0; i < v.Length; i++) {
                EntryPoint.queue.CreateSymbolicLink(v[i], set[i]);
              }
              return set;
            })]
        };
        existing = [.. existing, assoc];
        fileRecords[pair.Key] = existing;
      }
      else
      {
        FileAssociation assoc = new()
        {
          AssociatedMod = name,
          Files = [..pair.Value.SelectMany((v, index) => {
              string[] set = GetFirstValidPatchSet(v, index);
              ModManager.existing.AddRange(set);
              for (int i = 0; i < v.Length; i++) {
                EntryPoint.queue.CreateSymbolicLink(v[i], set[i]);
              }
              return set;
            })]
        };
        fileRecords[pair.Key] = [assoc];
      }
    }
    modState[name] = state with { Enabled = true };
    SaveData();
  }

  public void CheckForPatchGaps()
  {
    foreach (KeyValuePair<string, FileAssociation[]> patches in fileRecords)
    {
      HashSet<int> foundPatches = [];
      foreach (FileAssociation patch in patches.Value)
      {
        HashSet<int> subNumbers = [];
        foreach (string file in patch.Files)
        {
          Match match = FileNumRegex.Match(file);
          if (match.Success)
          {
            if (int.TryParse(match.Value, out int num))
            {
              _ = subNumbers.Add(num);
            }
          }
        }
        foundPatches = [.. foundPatches, .. subNumbers];
      }
      Dictionary<(int patch, FileAssociation associated), string[]> markedForMove = [];
      bool foundGap = false;
      int lastPatch = -1;
      foreach (int patchNum in foundPatches.OrderBy((v) => v))
      {
        int diff = Math.Abs(patchNum - lastPatch);
        if (diff > 1)
        {
          foundGap = true;
          break;
        }
        lastPatch = patchNum;
      }
      if (foundGap)
      {
        foreach (FileAssociation assoc in patches.Value)
        {
          foreach (string file in assoc.Files)
          {
            Match match = FileNumRegex.Match(file);
            if (match.Success)
            {
              if (int.TryParse(match.Value, out int num))
              {
                if (num > lastPatch)
                {
                  if (markedForMove.TryGetValue((num, assoc), out string[] files))
                  {
                    markedForMove[(num, assoc)] = [.. files, file];
                  }
                  else
                  {
                    markedForMove[(num, assoc)] = [file];
                  }
                }
              }
            }
          }
        }
        Dictionary<string, FileAssociation> newAssociations = [];
        foreach (KeyValuePair<(int patch, FileAssociation associated), string[]> kvp in markedForMove)
        {
          if (newAssociations.TryGetValue(kvp.Key.associated.AssociatedMod, out FileAssociation assoc))
          {
            string[][] sets = ProcessFilenamesIntoPatchSets([.. kvp.Value]);
            string[] newF = [];
            foreach (string[] set in sets)
            {
              string[] newFiles = GetFirstValidPatchSet(set, 0);
              for (int i = 0; i < set.Length; i++)
              {
                EntryPoint.queue.Move(set[i], newFiles[i]);
                existing.Add(newFiles[i]);
                _ = existing.Remove(set[i]);
              }
              //EntryPoint.queue.WaitForEmpty();
              newF = [.. newF, .. newFiles];
            }
            newAssociations[kvp.Key.associated.AssociatedMod] = assoc with { Files = [.. assoc.Files, .. newF] };
          }
          else
          {
            string[][] sets = ProcessFilenamesIntoPatchSets([.. kvp.Value]);
            string[] newF = [];
            foreach (string[] set in sets)
            {
              string[] newFiles = GetFirstValidPatchSet(set, 0);
              for (int i = 0; i < set.Length; i++)
              {
                EntryPoint.queue.Move(set[i], newFiles[i]);
                existing.Add(newFiles[i]);
                _ = existing.Remove(set[i]);
              }
              //EntryPoint.queue.WaitForEmpty();
              newF = [.. newF, .. newFiles];
            }
            newAssociations[kvp.Key.associated.AssociatedMod] = new FileAssociation()
            {
              AssociatedMod = kvp.Key.associated.AssociatedMod,
              Files = newF
            };
          }
        }
        foreach (KeyValuePair<string, FileAssociation> kvp in newAssociations)
        {
          FileAssociation[] ex = fileRecords[patches.Key];
          ex = [.. ex.Where((v) => v.AssociatedMod != kvp.Key)];
          ex = [.. ex, kvp.Value];
          fileRecords[patches.Key] = ex;
        }
        EntryPoint.queue.WaitForEmpty();
      }
    }
    SaveData();
  }

  public void DisableMod(string name)
  {
    ModJson state = modState[name];
    if (!state.Enabled)
    {
      return;
    }

    ArsenalMod mod = mods.First((m) => m.Guid == name);
    string[][] filenames = mod.Manifest.HasValue ? ProcessChoicesIntoPatchSets(mod) : ProcessFilenamesIntoPatchSets([.. mod.Files!]);
    Dictionary<string[], string> basenames = [];
    foreach (string[] set in filenames)
    {
      string fset = set.First();
      string fname = new FileInfo(fset).Name;
      string withoutExt = fname.Contains('.') ? fname[..fname.IndexOf('.')] : fname;
      basenames[set] = withoutExt;
    }
    HashSet<string> check = [];
    Dictionary<string, HashSet<string>> deleting = [];
    foreach (string[] set in filenames)
    {
      string basename = basenames[set];
      bool valueGotten = fileRecords.TryGetValue(basename, out FileAssociation[]? value);
      FileAssociation[] associations = valueGotten ? value! : [];
      FileAssociation? ourAssociation = null;
      foreach (FileAssociation assoc in associations)
      {
        if (assoc.AssociatedMod == name)
        {
          ourAssociation = assoc;
          break;
        }
      }
      if (ourAssociation.HasValue)
      {
        if (valueGotten)
        {
          _ = check.Add(basename);
        }
        foreach (string file in ourAssociation.Value.Files)
        {
          if (deleting.TryGetValue(basename, out HashSet<string>? v))
          {
            _ = v.Add(file);
          }
          else
          {
            deleting[basename] = [file];
          }
        }
      }
    }
    foreach (KeyValuePair<string[], string> basename in basenames)
    {
      foreach (string file in deleting[basename.Value])
      {
        if (File.Exists(file))
        {
          EntryPoint.queue.Delete(file);
        }
      }
    }
    EntryPoint.queue.WaitForEmpty();
    foreach (string b in check)
    {
      FileAssociation[] assoc = fileRecords[b];
      assoc = [.. assoc.Where((v) => v.AssociatedMod != mod.Guid)];
      if (assoc.Length > 0)
      {
        fileRecords[b] = assoc;
      }
      else
      {
        _ = fileRecords.Remove(b);
      }
    }
    modState[name] = state with { Enabled = false };
    SaveData();
  }

  public void UninstallMod(string name)
  {
    DisableMod(name);
    _ = modState.Remove(name);
    ArsenalMod mod = mods.First((m) => m.Guid == name);
    string ModDirectoryPath = Path.Join(ModHolder, mod.FolderName);
    Directory.Delete(ModDirectoryPath, true);
    _ = mods.Remove(mod);
    _ = modChoices.Remove(name);
    _ = aliases.Remove(name);
    priorities = [.. priorities.Where(v => v != name)];
    SaveData();
  }

  private void ProcessMods()
  {
    if (!Directory.Exists(ModHolder))
    {
      _ = Directory.CreateDirectory(ModHolder);
      return;
    }
    string[] directories = [.. Directory.EnumerateDirectories(ModHolder)];
    foreach (string dir in directories)
    {
      _ = ProcessMod(dir);
    }

    ArsenalMod[] arr = [.. mods];
    Array.Sort(arr, static (x, y) => string.Compare(x.Name, y.Name));
    mods = [.. arr];
  }

  internal ArsenalMod ProcessMod(string path)
  {
    bool hasManifest = File.Exists(Path.Join(path, "manifest.json"));
    if (hasManifest)
    {
      ArsenalMod mod = ModWithManifest(path);
      mods = [.. mods.Where((v) => v.Guid != mod.Guid)];
      mods.Add(mod);
      modAdded.Invoke(null, mod);
      if (!priorities.Contains(mod.Guid))
      {
        priorities = [.. priorities, mod.Guid];
      }
      return mod;
    }
    else
    {
      ArsenalMod mod = ModWithoutManifest(path);
      mods = [.. mods.Where((v) => v.Guid != mod.Guid)];
      mods.Add(mod);
      modAdded.Invoke(null, mod);
      if (!priorities.Contains(mod.Guid))
      {
        priorities = [.. priorities, mod.Guid];
      }
      return mod;
    }
  }

  private static string GetName(string path)
  {
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

  private static ManifestChoices[] GenerateDefaultManifestChoices(ArsenalManifest mod)
  {
    List<ManifestChoices> choices = [];
    if (mod.Options == null || mod.Options.Length == 0)
    {
      return [];
    }

    foreach (object opt in mod.Options)
    {
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
      else if (opt is string opti)
      {
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

  private static void ApplyChoices(ManifestChoices[] choices, string[] paths)
  {
    foreach (ManifestChoices choice in choices)
    {
      if (paths.Contains(choice.Name))
      {
        choice.Chosen = true;
      }
      if (choice.SubChoices != null)
      {
        ApplySubChoices(choice.SubChoices, paths, choice.Name + "/");
      }
    }
  }

  private static void ApplySubChoices(ManifestChoices[] choices, string[] paths, string currentPath)
  {
    foreach (ManifestChoices choice in choices)
    {
      if (paths.Contains(currentPath + choice.Name))
      {
        choice.Chosen = true;
      }
    }
  }

  private static ManifestChoices[] GetSubChoices(object[] options)
  {
    List<ManifestChoices> choices = [];
    foreach (object opt in options)
    {
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
      else if (opt is string opti)
      {
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

  private ArsenalMod ModWithManifest(string path)
  {
    ArsenalManifest manifest = JsonConvert.DeserializeObject<ArsenalManifest>(File.ReadAllText(Path.Join(path, "manifest.json")));
    if (!modState.TryGetValue(manifest.Guid, out ModJson state))
    {
      state = new()
      {
        Version = "1.0.0",
        Enabled = false
      };
      modState[manifest.Guid] = state;
    }
    string[]? files = null;
    if (!processedChoices.ContainsKey(manifest.Guid))
    {
      ManifestChoices[] choices = GenerateDefaultManifestChoices(manifest);
      if (choices.Length > 0)
      {
        if (modChoices.TryGetValue(manifest.Guid, out string[]? value))
        {
          ApplyChoices(choices, value);
        }

        processedChoices[manifest.Guid] = choices;
      }
      else
      {
        files = [.. GetFolderRecursively(path).Where(static (v) => FileNumRegex.Match(v).Success)];
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

  private ArsenalMod ModWithoutManifest(string path)
  {
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
      Files = [.. Directory.EnumerateFiles(path).Where(static (v) => FileNumRegex.Match(v).Success)],
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