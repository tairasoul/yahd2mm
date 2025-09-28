using System.IO.Pipes;
using System.Text;
using Aspose.Zip.SevenZip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace yahd2mm;

struct OldNexusData {
  public string id;
  public string mainMod;
}

struct NexusData {
  public string modName;
  public string[] associatedGuids;
}

class Manager {
  static readonly string DownloadHolder = Path.Join(ModManager.yahd2mm_basepath, "downloads");
  static readonly string NexusIds = Path.Join(ModManager.yahd2mm_basepath, "nexus-ids.json");
  internal Dictionary<string, NexusData> nexusIds;
  internal EventHandler<DownloadProgress> FileDownloadProgress = (_, __) => {};
  internal EventHandler<(string, string, string)> FileDownloaded = (_, __) => {};
  internal Dictionary<string, string> modNames = [];
  internal DownloadManager downloadManager;
  internal ModManager modManager;
  internal ModpackManager modpackManager;
  internal Dictionary<string, string> nexusReverse = [];

  private static void Migrate() {
    if (!File.Exists(NexusIds)) return;
    JObject baseObj = JObject.Parse(File.ReadAllText(NexusIds).Trim());
    if (!baseObj.HasValues) return;
    JToken firstToken = baseObj.First.First;
    if (firstToken["id"] != null && firstToken["mainMod"] != null) {
      Dictionary<string, NexusData> newDict = [];
      foreach (KeyValuePair<string, OldNexusData> oldPair in baseObj.ToObject<Dictionary<string, OldNexusData>>()) {
        if (newDict.TryGetValue(oldPair.Value.id, out NexusData newData)) {
          newDict[oldPair.Value.id] = newData with { associatedGuids = [.. newData.associatedGuids, oldPair.Key] };
        }
        else {
          newDict[oldPair.Value.id] = new NexusData()
          {
            associatedGuids = [oldPair.Key],
            modName = oldPair.Value.mainMod
          };
        }
      }
      File.WriteAllText(NexusIds, JsonConvert.SerializeObject(newDict));
    }
  }

  private void PopulateReverseLookupTable() {
    foreach (KeyValuePair<string, NexusData> data in nexusIds) {
      foreach (string guid in data.Value.associatedGuids) {
        nexusReverse[guid] = data.Key;
      }
    }
  }

  public Manager() {
    downloadManager = new(this);
    modManager = new(this);
    modpackManager = new();
    Migrate();
    nexusIds = File.Exists(NexusIds) ? JsonConvert.DeserializeObject<Dictionary<string, NexusData>>(File.ReadAllText(NexusIds).Trim()) ?? [] : [];
    PopulateReverseLookupTable();
    if (!Directory.Exists(DownloadHolder)) {
      Directory.CreateDirectory(DownloadHolder);
    }
  }

  internal void SaveData() {
    File.WriteAllText(NexusIds, JsonConvert.SerializeObject(nexusIds));
  }

  internal void InstallFile(string file) {
    if (Directory.Exists(file)) {
      DirectoryInfo info = new(file);
      Directory.Move(file, Path.Join(ModManager.ModHolder, info.Name));
      modManager.ProcessMod(Path.Join(ModManager.ModHolder, info.Name));
      HD2Mod[] mods = [.. modManager.mods ];
      Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
      modManager.mods = [.. mods];
      return;
    };
    try
    {
      using Stream stream = File.OpenRead(file);
      using IReader reader = ReaderFactory.Open(stream);
      string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(file));
      if (Directory.Exists(outputDir))
        Directory.Delete(outputDir, true);
      Directory.CreateDirectory(outputDir);
      reader.WriteAllToDirectory(outputDir, new()
      {
        ExtractFullPath = true,
        Overwrite = true
      });
      string[] files = [.. Directory.EnumerateFiles(outputDir), .. Directory.EnumerateDirectories(outputDir)];
      string guid;
      if (files.Length == 1 && Directory.Exists(files[0]))
      {
        Directory.Move(files[0], Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
        Directory.Delete(outputDir);
        HD2Mod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
        guid = m.Guid;
      }
      else
      {
        HD2Mod m = modManager.ProcessMod(outputDir);
        guid = m.Guid;
      }
      modManager.modState[guid] = modManager.modState[guid] with { InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
      HD2Mod[] mods = [ .. modManager.mods ];
      Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
      modManager.mods = [.. mods];
      if (Config.cfg.ActivateOnInstall) {
        modManager.EnableMod(guid);
      }
      if (Config.cfg.ActivateOptionsOnInstall) {
        modManager.ActivateAllOptionsAndSubOptions(guid);
      }
    }
    catch (InvalidFormatException) {
      using Stream stream = File.OpenRead(file);
      using SevenZipArchive archive = new(stream);
      string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(file));
      if (Directory.Exists(outputDir))
        Directory.Delete(outputDir, true);
      Directory.CreateDirectory(outputDir);
      archive.ExtractToDirectory(outputDir);
      string[] files = [.. Directory.EnumerateFiles(outputDir), .. Directory.EnumerateDirectories(outputDir)];
      string guid;
      if (files.Length == 1 && Directory.Exists(files[0]))
      {
        Directory.Move(files[0], Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
        Directory.Delete(outputDir);
        HD2Mod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));

        guid = m.Guid;
      }
      else
      {
        HD2Mod m = modManager.ProcessMod(outputDir);
        guid = m.Guid;
      }
      modManager.modState[guid] = modManager.modState[guid] with { InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
      HD2Mod[] mods = [ .. modManager.mods ];
      Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
      modManager.mods = [.. mods];
      if (Config.cfg.ActivateOnInstall) {
        modManager.EnableMod(guid);
      }
      if (Config.cfg.ActivateOptionsOnInstall) {
        modManager.ActivateAllOptionsAndSubOptions(guid);
      }
    }
  }

  private void DownloadFile(string nxm_url) {
    Console.WriteLine($"Downloading {nxm_url}");
    downloadManager.StartDownload(nxm_url, DownloadHolder);
    if (Config.cfg.OpenDownloadsOnNew)
      EntryPoint.SwitchToDownloads = true;
    void d(object? sender, (string, string, string, string) output)
    {
      if (output.Item2 != nxm_url) return;
      ProcessedLink l = DownloadManager.ProcessLink(output.Item2);
      downloadManager.DownloadFinished -= d;
      string ModName = "ExtractFailed";
      try
      {
        using Stream stream = File.OpenRead(output.Item3);
        using IReader reader = ReaderFactory.Open(stream);
        string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(output.Item1));
        if (Directory.Exists(outputDir))
        {
          string folderName = new DirectoryInfo(outputDir).Name;
          HD2Mod? mod = modManager.mods.Where(m => m.FolderName == folderName).Cast<HD2Mod?>().FirstOrDefault();
          if (mod.HasValue)
          {
            string modGuid = mod.Value.Guid;
            KeyValuePair<string, NexusData> entry = nexusIds.FirstOrDefault(kvp => kvp.Value.associatedGuids.Contains(modGuid));
            if (entry.Key != null)
            {
              if (entry.Key == l.modId)
              {
                Directory.Delete(outputDir, true);
              }
              else
              {
                outputDir = $"{outputDir} ({l.modId})";
              }
            }
            else
            {
              Directory.Delete(outputDir, true);
            }
          }
          else
          {
            Directory.Delete(outputDir, true);
          }
        }
        Directory.CreateDirectory(outputDir);
        reader.WriteAllToDirectory(outputDir, new()
        {
          ExtractFullPath = true,
          Overwrite = true
        });
        string[] files = [.. Directory.EnumerateFiles(outputDir), .. Directory.EnumerateDirectories(outputDir)];
        string guid;
        if (files.Length == 1 && Directory.Exists(files[0]))
        {
          Directory.Move(files[0], Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          Directory.Delete(outputDir);
          HD2Mod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          ModName = m.Name;
          guid = m.Guid;
        }
        else
        {
          HD2Mod m = modManager.ProcessMod(outputDir);
          ModName = m.Name;
          guid = m.Guid;
        }
        modManager.modState[guid] = modManager.modState[guid] with { Version = output.Item4, InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
        modNames[output.Item2] = ModName;
        if (nexusIds.TryGetValue(l.modId, out NexusData existingData)) {
          nexusIds[l.modId] = existingData with { associatedGuids = [.. existingData.associatedGuids, guid] };
        }
        else {
          nexusIds[l.modId] = new NexusData()
          {
            modName = downloadManager.progresses[output.Item2].mainModName,
            associatedGuids = [guid]
          };
        }
        modManager.SaveData();
        SaveData();
        HD2Mod[] mods = [ .. modManager.mods ];
        Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
        modManager.mods = [.. mods];
        if (Config.cfg.ActivateOnInstall) {
          modManager.EnableMod(guid);
        }
        if (Config.cfg.ActivateOptionsOnInstall) {
          modManager.ActivateAllOptionsAndSubOptions(guid);
        }
      }
      catch (InvalidFormatException) {
        using Stream stream = File.OpenRead(output.Item3);
        using SevenZipArchive archive = new(stream);
        string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(output.Item1));
        if (Directory.Exists(outputDir))
        {
          string folderName = new DirectoryInfo(outputDir).Name;
          HD2Mod? mod = modManager.mods.Where(m => m.FolderName == folderName).Cast<HD2Mod?>().FirstOrDefault();
          if (mod.HasValue)
          {
            string modGuid = mod.Value.Guid;
            KeyValuePair<string, NexusData> entry = nexusIds.FirstOrDefault(kvp => kvp.Value.associatedGuids.Contains(modGuid));
            if (entry.Key != null)
            {
              if (entry.Key == l.modId)
              {
                Directory.Delete(outputDir, true);
              }
              else
              {
                outputDir = $"{outputDir} ({l.modId})";
              }
            }
            else
            {
              Directory.Delete(outputDir, true);
            }
          }
          else
          {
            Directory.Delete(outputDir, true);
          }
        }
        Directory.CreateDirectory(outputDir);
        archive.ExtractToDirectory(outputDir);
        string[] files = [.. Directory.EnumerateFiles(outputDir), .. Directory.EnumerateDirectories(outputDir)];
        string guid;
        if (files.Length == 1 && Directory.Exists(files[0]))
        {
          Directory.Move(files[0], Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          Directory.Delete(outputDir);
          HD2Mod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          ModName = m.Name;
          guid = m.Guid;
        }
        else
        {
          HD2Mod m = modManager.ProcessMod(outputDir);
          ModName = m.Name;
          guid = m.Guid;
        }
        modManager.modState[guid] = modManager.modState[guid] with { Version = output.Item4, InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
        modNames[output.Item2] = ModName;
        if (nexusIds.TryGetValue(l.modId, out NexusData existingData)) {
          nexusIds[l.modId] = existingData with { associatedGuids = [.. existingData.associatedGuids, guid] };
        }
        else {
          nexusIds[l.modId] = new NexusData()
          {
            modName = downloadManager.progresses[output.Item2].mainModName,
            associatedGuids = [guid]
          };
        }
        modManager.SaveData();
        SaveData();
        HD2Mod[] mods = [.. modManager.mods];
        Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
        modManager.mods = [.. mods];
        if (Config.cfg.ActivateOnInstall) {
          modManager.EnableMod(guid);
        }
        if (Config.cfg.ActivateOptionsOnInstall) {
          modManager.ActivateAllOptionsAndSubOptions(guid);
        }
      }
      finally
      {
        EntryPoint.queue.Delete(output.Item3);
      }
    }
    downloadManager.DownloadFinished += d;
  }

  internal NamedPipeServerStream server = new("yahd2mm.pipe", PipeDirection.In);

  public void BeginListeningPipe() {
    Task.Run(async () =>
    {
      while (true) {
        await server.WaitForConnectionAsync();
        if (server.IsConnected) {
          var reader = new StreamReader(server, Encoding.UTF8);
          string? message = await reader.ReadLineAsync();
          Console.WriteLine($"Message: {message}");
          if (message != null && message.StartsWith("nxm://")) {
            Console.WriteLine("Beginning download");
            DownloadFile(message);
          }
          server.Disconnect();
        }
      }
    });
  }
}