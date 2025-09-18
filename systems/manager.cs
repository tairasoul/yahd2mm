using System.IO.Pipes;
using System.Text;
using Aspose.Zip.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace yahd2mm;

struct FinishedDownload {
  public long Size;
  public string Filename;
  public string Modname;
}

class Manager {
  static readonly string DownloadHolder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "downloads");
  internal EventHandler<DownloadProgress> FileDownloadProgress = (_, __) => {};
  internal EventHandler<(string, string, string)> FileDownloaded = (_, __) => {};
  internal DownloadManager downloadManager;
  internal ModManager modManager;
  internal ModpackManager modpackManager;
  internal Dictionary<string, DownloadProgress> progresses = [];
  internal Dictionary<string, FinishedDownload> completed = [];
  public Manager() {
    downloadManager = new();
    modManager = new();
    modpackManager = new();
    if (!Directory.Exists(DownloadHolder)) {
      Directory.CreateDirectory(DownloadHolder);
    }
    foreach (string file in Directory.EnumerateFiles(DownloadHolder)) {
      File.Delete(file);
    }
    foreach (string dir in Directory.EnumerateDirectories(DownloadHolder)) {
      Directory.Delete(dir, true);
    }
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
      ArsenalMod[] mods = [ .. modManager.mods ];
      Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
      modManager.mods = [.. mods];
      if (Config.cfg.ActivateOnInstall) {
        modManager.EnableMod(guid);
      }
      if (Config.cfg.ActivateOptionsOnInstall) {
        modManager.ActivateAllOptions(guid);
      }
    }
    catch (InvalidFormatException) {
      using Stream stream = File.OpenRead(file);
      using SevenZipArchive archive = new(stream);
      string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(file));
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
      ArsenalMod[] mods = [ .. modManager.mods ];
      Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
      modManager.mods = [.. mods];
      if (Config.cfg.ActivateOnInstall) {
        modManager.EnableMod(guid);
      }
      if (Config.cfg.ActivateOptionsOnInstall) {
        modManager.ActivateAllOptions(guid);
      }
    }
  }

  private void DownloadFile(string nxm_url) {
    Console.WriteLine($"Downloading {nxm_url}");
    Progress<DownloadProgress> progress = new((v) => progresses[v.modName] = v);
    downloadManager.StartDownload(nxm_url, DownloadHolder, progress);
    EntryPoint.SwitchToDownloads = true;
    void d(object? sender, (string, string, string) output)
    {
      if (output.Item2 != nxm_url) return;
      downloadManager.DownloadFinished -= d;
      string ModName = "Failed to extract.";
      try
      {
        using Stream stream = File.OpenRead(output.Item3);
        using IReader reader = ReaderFactory.Open(stream);
        string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(output.Item1));
        if (Directory.Exists(outputDir))
          Directory.Delete(outputDir);
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
        ArsenalMod[] mods = [ .. modManager.mods ];
        Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
        modManager.mods = [.. mods];
        if (Config.cfg.ActivateOnInstall) {
          modManager.EnableMod(guid);
        }
        if (Config.cfg.ActivateOptionsOnInstall) {
          modManager.ActivateAllOptions(guid);
        }
      }
      catch (InvalidFormatException) {
        try
        {
          using Stream stream = File.OpenRead(output.Item3);
          using SevenZipArchive archive = new(stream);
          string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(output.Item1));
          if (Directory.Exists(outputDir))
            Directory.Delete(outputDir);
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
          ArsenalMod[] mods = [.. modManager.mods];
          Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
          modManager.mods = [.. mods];
          if (Config.cfg.ActivateOnInstall) {
            modManager.EnableMod(guid);
          }
          if (Config.cfg.ActivateOptionsOnInstall) {
            modManager.ActivateAllOptions(guid);
          }
        }
        catch (Exception) { }
      }
      finally
      {
        DownloadProgress p = progresses[output.Item1];
        FinishedDownload d = new()
        {
          Filename = output.Item1,
          Modname = ModName,
          Size = p.TotalBytes
        };
        completed[output.Item1] = d;
        progresses.Remove(output.Item1);
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