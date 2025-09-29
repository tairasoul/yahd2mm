using System.Collections.Concurrent;
using Aspose.Zip.SevenZip;
using Newtonsoft.Json;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace yahd2mm;

public class DownloadProgress(string modName, long bytesDownloaded, long totalBytes)
{
  public string modName = modName;
  public long BytesDownloaded { get; } = bytesDownloaded;
  public long TotalBytes { get; } = totalBytes;
  public float ProgressPercentage =>  BytesDownloaded / TotalBytes;
}

struct ConcurrentDownload {
  public Task task;
}

struct DownloadLinkAPIResponse {
  public string URI;
  public string name;
  public string short_name;
}

struct ProcessedLink {
  public string modId;
  public string fileId;
  public string key;
  public string expires;
}

struct ModInfoAPIResponse {
  public string name;
  public string version;
}

struct ModFileAPIResponse {
  public string version;
  public string name;
}

enum DownloadStatus : uint {
  Done,
  Active,
  Paused,
  Cancelled,
  Expired
}

struct DownloadState {
  public DownloadStatus status;
  public long totalBytes;
  public long bytesRead;
  public string downloadURL;
  public string outputPath;
  public string modName;
  public string version;
  public string mainModName;
}

struct ActiveDownload {
  public string download_url;
  public string nxm_url;
  public string outputPath;
  public string version;
  public string modName;
  public string mainModName;
  public long totalBytes;
  public DownloadStatus currentState;
}

class DownloadManager {
  internal ConcurrentDictionary<string, DownloadState> progresses = [];
  internal static string ActiveDownloadsPath = Path.Join(ModManager.yahd2mm_basepath, "active-downloads.json");
  internal ConcurrentBag<ActiveDownload> activeDownloads = File.Exists(ActiveDownloadsPath) ? JsonConvert.DeserializeObject<ConcurrentBag<ActiveDownload>>(File.ReadAllText(ActiveDownloadsPath)) ?? [] : [];
  readonly HttpClient client;

  public EventHandler<(string, string, string, string)> DownloadFinished = (_, __) => {};

  public DownloadManager(Manager manager) {
    client = new();
    client.DefaultRequestHeaders.Add("User-Agent", "yahd2mm/0.5.2 .NET/9.0");
    client.DefaultRequestHeaders.Add("apikey", EntryPoint.APIKey);
    List<string> urlsToResume = [];
    foreach (ActiveDownload download in activeDownloads) {
      long bytesRead = new FileInfo(download.outputPath).Length;
      DownloadState state = new()
      {
        bytesRead = bytesRead,
        totalBytes = download.totalBytes,
        downloadURL = download.download_url,
        outputPath = download.outputPath,
        status = download.currentState,
        mainModName = download.mainModName,
        modName = download.modName,
        version = download.version
      };
      progresses[download.nxm_url] = state;
      if (download.currentState == DownloadStatus.Active)
        urlsToResume.Add(download.nxm_url);
    }
    foreach (string resume in urlsToResume) {
      AddFinishedListener(resume, manager);
      ResumeDownload(resume);
    }
  }

  private void AddFinishedListener(string url, Manager manager) {
    void d(object? sender, (string, string, string, string) output)
    {
      if (output.Item2 != url) return;
      ProcessedLink l = ProcessLink(output.Item2);
      DownloadFinished -= d;
      string ModName = "ExtractFailed";
      try
      {
        using Stream stream = File.OpenRead(output.Item3);
        using IReader reader = ReaderFactory.Open(stream);
        string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(output.Item1));
        if (Directory.Exists(outputDir))
        {
          string folderName = new DirectoryInfo(outputDir).Name;
          HD2Mod? mod = manager.modManager.mods.Where(m => m.FolderName == folderName).Cast<HD2Mod?>().FirstOrDefault();
          if (mod.HasValue)
          {
            string modGuid = mod.Value.Guid;
            KeyValuePair<string, NexusData> entry = manager.nexusIds.FirstOrDefault(kvp => kvp.Value.associatedGuids.Contains(modGuid));
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
          HD2Mod m = manager.modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          ModName = m.Name;
          guid = m.Guid;
        }
        else
        {
          HD2Mod m = manager.modManager.ProcessMod(outputDir);
          ModName = m.Name;
          guid = m.Guid;
        }
        manager.modManager.modState[guid] = manager.modManager.modState[guid] with { Version = output.Item4, InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
        manager.modNames[output.Item2] = ModName;
        if (manager.nexusIds.TryGetValue(l.modId, out NexusData existingData)) {
          manager.nexusIds[l.modId] = existingData with { associatedGuids = [.. existingData.associatedGuids, guid] };
        }
        else {
          manager.nexusIds[l.modId] = new NexusData()
          {
            modName = progresses[output.Item2].mainModName,
            associatedGuids = [guid]
          };
        }
        manager.nexusReverse[guid] = l.modId;
        manager.modManager.SaveData();
        SaveData();
        HD2Mod[] mods = [ .. manager.modManager.mods ];
        Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
        manager.modManager.mods = [.. mods];
        if (Config.cfg.ActivateOnInstall) {
          manager.modManager.EnableMod(guid);
        }
        if (Config.cfg.ActivateOptionsOnInstall) {
          manager.modManager.ActivateAllOptionsAndSubOptions(guid);
        }
      }
      catch (InvalidFormatException) {
        using Stream stream = File.OpenRead(output.Item3);
        using SevenZipArchive archive = new(stream);
        string outputDir = Path.Join(ModManager.ModHolder, Path.GetFileNameWithoutExtension(output.Item1));
        if (Directory.Exists(outputDir))
        {
          string folderName = new DirectoryInfo(outputDir).Name;
          HD2Mod? mod = manager.modManager.mods.Where(m => m.FolderName == folderName).Cast<HD2Mod?>().FirstOrDefault();
          if (mod.HasValue)
          {
            string modGuid = mod.Value.Guid;
            KeyValuePair<string, NexusData> entry = manager.nexusIds.FirstOrDefault(kvp => kvp.Value.associatedGuids.Contains(modGuid));
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
          HD2Mod m = manager.modManager.ProcessMod(Path.Join(ModManager.ModHolder, new DirectoryInfo(files[0]).Name));
          ModName = m.Name;
          guid = m.Guid;
        }
        else
        {
          HD2Mod m = manager.modManager.ProcessMod(outputDir);
          ModName = m.Name;
          guid = m.Guid;
        }
        manager.modManager.modState[guid] = manager.modManager.modState[guid] with { Version = output.Item4, InstalledAt = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds() };
        manager.modNames[output.Item2] = ModName;
        if (manager.nexusIds.TryGetValue(l.modId, out NexusData existingData)) {
          manager.nexusIds[l.modId] = existingData with { associatedGuids = [.. existingData.associatedGuids, guid] };
        }
        else {
          manager.nexusIds[l.modId] = new NexusData()
          {
            modName = progresses[output.Item2].mainModName,
            associatedGuids = [guid]
          };
        }
        manager.nexusReverse[guid] = l.modId;
        manager.modManager.SaveData();
        SaveData();
        HD2Mod[] mods = [.. manager.modManager.mods];
        Array.Sort(mods, static (x, y) => string.Compare(x.Name, y.Name));
        manager.modManager.mods = [.. mods];
        if (Config.cfg.ActivateOnInstall) {
          manager.modManager.EnableMod(guid);
        }
        if (Config.cfg.ActivateOptionsOnInstall) {
          manager.modManager.ActivateAllOptionsAndSubOptions(guid);
        }
      }
      finally
      {
        EntryPoint.queue.Delete(output.Item3);
      }
    }
    DownloadFinished += d;
  }

  internal void SaveData() {
    File.WriteAllText(ActiveDownloadsPath, JsonConvert.SerializeObject(activeDownloads));
  }

  internal static ProcessedLink ProcessLink(string url) {
    string firstReplaced = url.Replace("nxm://helldivers2/mods/", "");
    string[] split1 = firstReplaced.Split('?');
    string[] baseurl = [.. split1[0].Split("/").Where((v) => v != "files")];
    string[] options = split1[1].Split("&");
    string modId = baseurl[0];
    string fileId = baseurl[1];
    string key = null!;
    string expires = null!;
    foreach (string option in options) {
      if (option.StartsWith("key=")) {
        key = option.Replace("key=", "");
        continue;
      }
      if (option.StartsWith("expires=")) {
        expires = option.Replace("expires=", "");
        continue;
      }
    }
    ProcessedLink link = new()
    {
      modId = modId,
      fileId = fileId,
      key = key!,
      expires = expires!
    };
    return link;
  }

  private static string? GetFileNameFromUrl(string url)
  {
    try
    {
      Uri uri = new(url);
      string path = uri.AbsolutePath;
      string filename = Path.GetFileName(path);
      return !string.IsNullOrEmpty(filename) ? filename : null;
    }
    catch
    {
      return null;
    }
  }

  private async Task<DownloadLinkAPIResponse> GetDownloadLink(string url) {
    ProcessedLink link = ProcessLink(url);
    string constructed = $"https://api.nexusmods.com/v1/games/helldivers2/mods/{link.modId}/files/{link.fileId}/download_link.json?key={link.key}&expires={link.expires}";
    HttpResponseMessage message = await client.GetAsync(constructed);
    string raw = await message.Content.ReadAsStringAsync();
    DownloadLinkAPIResponse[] response = JsonConvert.DeserializeObject<DownloadLinkAPIResponse[]>(raw)!;
    return response!.First();
  }

  private async Task<ModInfoAPIResponse> GetModInfo(string url) {
    ProcessedLink link = ProcessLink(url);
    string id = link.modId;
    string api_url = $"https://api.nexusmods.com/v1/games/helldivers2/mods/{id}.json";
    HttpResponseMessage message = await client.GetAsync(api_url);
    string raw = await message.Content.ReadAsStringAsync();
    ModInfoAPIResponse response = JsonConvert.DeserializeObject<ModInfoAPIResponse>(raw);
    return response;
  }

  private async Task<ModFileAPIResponse> GetModFile(string url) {
    ProcessedLink link = ProcessLink(url);
    string api_url = $"https://api.nexusmods.com/v1/games/helldivers2/mods/{link.modId}/files/{link.fileId}.json";
    HttpResponseMessage message = await client.GetAsync(api_url);
    string raw = await message.Content.ReadAsStringAsync();
    ModFileAPIResponse response = JsonConvert.DeserializeObject<ModFileAPIResponse>(raw);
    return response;
  }

  private readonly List<string> resuming = [];

  private ConcurrentDownload ResumeFileDownload(string url) {
    if (resuming.Contains(url)) return new() { task = Task.CompletedTask };
    resuming.Add(url);
    Task mainTask = Task.Run(async () =>
    {
      DownloadState state = progresses[url];
      client.DefaultRequestHeaders.Range = new(state.bytesRead, null);
      using HttpResponseMessage response = await client.GetAsync(state.downloadURL, HttpCompletionOption.ResponseHeadersRead);
      client.DefaultRequestHeaders.Range = null;
      response.EnsureSuccessStatusCode();
      string fullPath = state.outputPath;
      long totalBytes = response.Content.Headers.ContentLength ?? -1L;
      long totalBytesRead = state.bytesRead;
      byte[] buffer = new byte[8192];
      using Stream contentStream = await response.Content.ReadAsStreamAsync();
      using FileStream fileStream = new(
        fullPath,
        FileMode.Append,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 8192,
        useAsync: true);
      progresses[url] = state with
      {
        status = DownloadStatus.Active
      };
      activeDownloads = [.. activeDownloads.Select((v) => {
        if (v.nxm_url == url) {
          return v with { currentState = DownloadStatus.Active };
        }
        return v;
      })];
      SaveData();
      int bytesRead;
      resuming.Remove(url);
      bool finished = false;
      while (true)
      {
        if (progresses[url].status == DownloadStatus.Paused) {
          activeDownloads = [.. activeDownloads.Select((v) => {
            if (v.nxm_url == url) {
              return v with { currentState = DownloadStatus.Paused };
            }
            return v;
          })];
          SaveData();
          break;
        }
        if (progresses[url].status == DownloadStatus.Cancelled) {
          activeDownloads = [..activeDownloads.Where((v) => v.nxm_url != url)];
          SaveData();
          fileStream.Flush();
          fileStream.Dispose();
          File.Delete(fullPath);
          progresses.Remove(url, out _);
          break;
        }
        bytesRead = await contentStream.ReadAsync(buffer);
        if (bytesRead <= 0) {
          finished = true;
          break;
        }
        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        totalBytesRead += bytesRead;
        progresses[url] = progresses[url] with
        {
          bytesRead = totalBytesRead
        };
      }
      if (finished)
      {
        fileStream.Flush();
        fileStream.Dispose();
        Console.WriteLine("download finished");
        activeDownloads = [..activeDownloads.Where((v) => v.nxm_url != url)];
        SaveData();
        progresses[url] = progresses[url] with { status = DownloadStatus.Done };
        DownloadFinished.Invoke(null, (Path.GetFileName(state.outputPath), url, fullPath, state.version));
      }
    });
    ConcurrentDownload download = new()
    {
      task = mainTask
    };
    return download;
  }

  private ConcurrentDownload DownloadFile(string url, string originalURL, string output) {
    Task mainTask = Task.Run(async () =>
    {
      using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
      response.EnsureSuccessStatusCode();
      ModFileAPIResponse modInfo = await GetModFile(originalURL);
      ModInfoAPIResponse mainModInfo = await GetModInfo(originalURL);
      string filename = Uri.UnescapeDataString(GetFileNameFromUrl(url) ?? "downloaded_mod.zip");
      filename = (modInfo.name + Path.GetExtension(filename)) ?? filename;
      string fullPath = Path.Join(output, filename);
      long totalBytes = response.Content.Headers.ContentLength ?? -1L;
      long totalBytesRead = 0L;
      byte[] buffer = new byte[8192];
      using Stream contentStream = await response.Content.ReadAsStreamAsync();
      using FileStream fileStream = new(
        fullPath,
        FileMode.Append,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 8192,
        useAsync: true);
      progresses[originalURL] = new()
      {
        totalBytes = totalBytes,
        bytesRead = 0L,
        downloadURL = url,
        outputPath = fullPath,
        modName = modInfo.name,
        status = DownloadStatus.Active,
        version = modInfo.version,
        mainModName = mainModInfo.name
      };
      activeDownloads.Add(new() {
        outputPath = fullPath,
        download_url = url,
        nxm_url = originalURL,
        version = modInfo.version,
        modName = modInfo.name,
        mainModName = mainModInfo.name,
        currentState = DownloadStatus.Active,
        totalBytes = totalBytes
      });
      SaveData();
      int bytesRead;
      bool finished = false;
      while (true)
      {
        if (progresses[originalURL].status == DownloadStatus.Paused) {
          activeDownloads = [.. activeDownloads.Select((v) => {
            if (v.nxm_url == url) {
              return v with { currentState = DownloadStatus.Paused };
            }
            return v;
          })];
          SaveData();
          break;
        }
        if (progresses[originalURL].status == DownloadStatus.Cancelled) {
          activeDownloads = [..activeDownloads.Where((v) => v.nxm_url != originalURL)];
          SaveData();
          fileStream.Flush();
          fileStream.Dispose();
          File.Delete(fullPath);
          progresses.Remove(url, out _);
          break;
        }
        bytesRead = await contentStream.ReadAsync(buffer);
        if (bytesRead <= 0) {
          finished = true;
          break;
        }
        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        totalBytesRead += bytesRead;
        progresses[originalURL] = progresses[originalURL] with
        {
          bytesRead = totalBytesRead
        };
      }
      if (finished)
      {
        fileStream.Flush();
        fileStream.Dispose();
        activeDownloads = [..activeDownloads.Where((v) => v.nxm_url != originalURL)];
        SaveData();
        Console.WriteLine("download finished");
        progresses[originalURL] = progresses[originalURL] with { status = DownloadStatus.Done };
        DownloadFinished.Invoke(null, (filename, originalURL, fullPath, modInfo.version));
      }
    });
    ConcurrentDownload download = new()
    {
      task = mainTask
    };
    return download;
  }

  public void ResumeDownload(string url) {
    Task.Run(async () =>
    {
      try {
        ConcurrentDownload download = ResumeFileDownload(url);
        await download.task;
      }
      catch (Exception e) {
        Console.WriteLine($"{e.Message} : {e.StackTrace}");
      }
    });
  }

  public void StartDownload(string url, string output) {
    Task.Run(async () =>
    {
      try
      {
        DownloadLinkAPIResponse response = await GetDownloadLink(url);
        ConcurrentDownload download = DownloadFile(response.URI, url, output);
        await download.task;
      }
      catch (Exception e) {
        Console.WriteLine($"{e.Message} : {e.StackTrace}");
      }
    });
  }
}