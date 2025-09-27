using System.IO.Pipes;
using System.Text;
using Aspose.Zip.SevenZip;
using Newtonsoft.Json;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace yahd2mm;

struct NexusData {
  public string id;
  public string mainMod;
}

class Manager {
  static readonly string DownloadHolder = Path.Join(ModManager.yahd2mm_basepath, "downloads");
  static readonly string NexusIds = Path.Join(ModManager.yahd2mm_basepath, "nexus-ids.json");
  internal Dictionary<string, NexusData> nexusIds = File.Exists(NexusIds) ? JsonConvert.DeserializeObject<Dictionary<string, NexusData>>(File.ReadAllText(NexusIds).Trim()) ?? [] : [];
  internal EventHandler<DownloadProgress> FileDownloadProgress = (_, __) => {};
  internal EventHandler<(string, string, string)> FileDownloaded = (_, __) => {};
  internal Dictionary<string, string> modNames = [];
  internal DownloadManager downloadManager;
  internal ModManager modManager;
  internal ModpackManager modpackManager;
  public Manager() {
    downloadManager = new(this);
    modManager = new();
    modpackManager = new();
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
      ArsenalMod[] mods = [.. modManager.mods ];
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
        ArsenalMod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
        guid = m.Guid;
      }
      else
      {
        ArsenalMod m = modManager.ProcessMod(outputDir);
        guid = m.Guid;
      }
      modManager.modState[guid] = modManager.modState[guid] with { InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
      ArsenalMod[] mods = [ .. modManager.mods ];
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
        ArsenalMod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));

        guid = m.Guid;
      }
      else
      {
        ArsenalMod m = modManager.ProcessMod(outputDir);
        guid = m.Guid;
      }
      modManager.modState[guid] = modManager.modState[guid] with { InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
      ArsenalMod[] mods = [ .. modManager.mods ];
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
          ArsenalMod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          ModName = m.Name;
          guid = m.Guid;
        }
        else
        {
          ArsenalMod m = modManager.ProcessMod(outputDir);
          ModName = m.Name;
          guid = m.Guid;
        }
        modManager.modState[guid] = modManager.modState[guid] with { Version = output.Item4, InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
        modNames[output.Item2] = ModName;
        nexusIds[guid] = new() {
          id = l.modId,
          mainMod = downloadManager.progresses[output.Item2].mainModName
        };
        modManager.SaveData();
        SaveData();
        ArsenalMod[] mods = [ .. modManager.mods ];
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
          Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);
        archive.ExtractToDirectory(outputDir);
        string[] files = [.. Directory.EnumerateFiles(outputDir), .. Directory.EnumerateDirectories(outputDir)];
        string guid;
        if (files.Length == 1 && Directory.Exists(files[0]))
        {
          Directory.Move(files[0], Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          Directory.Delete(outputDir);
          ArsenalMod m = modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          ModName = m.Name;
          guid = m.Guid;
        }
        else
        {
          ArsenalMod m = modManager.ProcessMod(outputDir);
          ModName = m.Name;
          guid = m.Guid;
        }
        modManager.modState[guid] = modManager.modState[guid] with { Version = output.Item4, InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
        modNames[output.Item2] = ModName;
        nexusIds[guid] = new() {
          id = l.modId,
          mainMod = downloadManager.progresses[output.Item2].mainModName
        };
        modManager.SaveData();
        SaveData();
        ArsenalMod[] mods = [.. modManager.mods];
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