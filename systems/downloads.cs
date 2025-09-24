using System.Collections.Concurrent;
using Newtonsoft.Json;

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

enum DownloadStatus {
  Done,
  Active,
  Paused,
  Cancelled
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

class DownloadManager {
  internal ConcurrentDictionary<string, DownloadState> progresses = [];
  readonly HttpClient client;

  public EventHandler<(string, string, string, string)> DownloadFinished = (_, __) => {};

  public DownloadManager() {
    client = new();
    client.DefaultRequestHeaders.Add("User-Agent", "yahd2mm/0.3.9 .NET/9.0");
    client.DefaultRequestHeaders.Add("apikey", EntryPoint.APIKey);
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
      var totalBytes = response.Content.Headers.ContentLength ?? -1L;
      var totalBytesRead = state.bytesRead;
      var buffer = new byte[8192];
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
      int bytesRead;
      resuming.Remove(url);
      bool cancelled = false;
      while (true)
      {
        if (progresses[url].status == DownloadStatus.Paused) {
          break;
        }
        if (progresses[url].status == DownloadStatus.Cancelled) {
          fileStream.Flush();
          fileStream.Dispose();
          File.Delete(fullPath);
          progresses.Remove(url, out _);
          break;
        }
        bytesRead = await contentStream.ReadAsync(buffer);
        if (bytesRead <= 0) {
          progresses[url] = progresses[url] with { status = DownloadStatus.Done };
          break;
        }
        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        totalBytesRead += bytesRead;
        progresses[url] = progresses[url] with
        {
          bytesRead = totalBytesRead
        };
      }
      if (cancelled) return;
      if (progresses[url].status == DownloadStatus.Done)
      {
        fileStream.Flush();
        Console.WriteLine("download finished");
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
      var totalBytes = response.Content.Headers.ContentLength ?? -1L;
      var totalBytesRead = 0L;
      var buffer = new byte[8192];
      using Stream contentStream = await response.Content.ReadAsStreamAsync();
      using FileStream fileStream = new(
        fullPath,
        FileMode.Create,
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
      int bytesRead;
      while (true)
      {
        if (progresses[originalURL].status == DownloadStatus.Paused) {
          break;
        }
        if (progresses[originalURL].status == DownloadStatus.Cancelled) {
          fileStream.Flush();
          fileStream.Dispose();
          File.Delete(fullPath);
          progresses.Remove(url, out _);
          break;
        }
        bytesRead = await contentStream.ReadAsync(buffer);
        if (bytesRead <= 0) {
          progresses[originalURL] = progresses[originalURL] with { status = DownloadStatus.Done };
          break;
        }
        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        totalBytesRead += bytesRead;
        progresses[originalURL] = progresses[originalURL] with
        {
          bytesRead = totalBytesRead
        };
      }
      if (progresses[originalURL].status == DownloadStatus.Done)
      {
        fileStream.Flush();
        Console.WriteLine("download finished");
        DownloadFinished.Invoke(null, (filename, originalURL, fullPath, modInfo.version));
        progresses[originalURL] = progresses[originalURL] with { status = DownloadStatus.Done };
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