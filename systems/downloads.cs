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

class DownloadManager {
  readonly List<ConcurrentDownload> downloads = [];
  readonly HttpClient client;

  public EventHandler<(string, string, string)> DownloadFinished = (_, __) => {};

  public DownloadManager() {
    client = new();
  }

  private ProcessedLink processLink(string url) {
    string firstReplaced = url.Replace("nxm://helldivers2/mods/", "");
    string[] split1 = firstReplaced.Split('?');
    string[] baseurl = split1[0].Split("/").Where((v) => v != "files").ToArray();
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

    private static string? GetFileNameFromHeader(HttpResponseMessage response)
    {
      if (response.Content.Headers.ContentDisposition?.FileNameStar != null)
      {
        return response.Content.Headers.ContentDisposition.FileNameStar;
      }
      if (response.Content.Headers.ContentDisposition?.FileName != null)
      {
        return response.Content.Headers.ContentDisposition.FileName.Trim('\"');
      }
      return null;
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
    ProcessedLink link = processLink(url);
    string constructed = $"https://api.nexusmods.com/v1/games/helldivers2/mods/{link.modId}/files/{link.fileId}/download_link.json?key={link.key}&expires={link.expires}";
    HttpRequestMessage request = new(HttpMethod.Get, constructed);
    request.Headers.Add("apikey", EntryPoint.APIKey);
    request.Headers.Add("User-Agent", "yahd2mm/0.2.4 .NET/9.0");
    HttpResponseMessage message = client.Send(request);
    string raw = await message.Content.ReadAsStringAsync();
    DownloadLinkAPIResponse[] response = JsonConvert.DeserializeObject<DownloadLinkAPIResponse[]>(raw)!;
    return response!.First();
  }

  private async Task<ModInfoAPIResponse> GetModInfo(string url) {
    ProcessedLink link = processLink(url);
    string id = link.modId;
    string api_url = $"https://api.nexusmods.com/v1/games/helldivers2/mods/{id}.json";
    HttpRequestMessage request = new(HttpMethod.Get, api_url);
    request.Headers.Add("User-Agent", "yahd2mm/0.2.4 .NET/9.0");
    request.Headers.Add("apikey", EntryPoint.APIKey);
    HttpResponseMessage message = client.Send(request);
    string raw = await message.Content.ReadAsStringAsync();
    ModInfoAPIResponse response = JsonConvert.DeserializeObject<ModInfoAPIResponse>(raw);
    return response;
  }

  private ConcurrentDownload DownloadFile(string url, string originalURL, string output, IProgress<DownloadProgress> progress = null!) {
    Task mainTask = Task.Run(async () =>
    {
      using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
      response.EnsureSuccessStatusCode();
      ModInfoAPIResponse modInfo = await GetModInfo(originalURL);
      string filename = Uri.UnescapeDataString(GetFileNameFromHeader(response) ?? GetFileNameFromUrl(url) ?? "downloaded_mod.zip");
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
      int bytesRead;
      while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
      {
        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        totalBytesRead += bytesRead;
        progress?.Report(new DownloadProgress(filename, totalBytesRead, totalBytes));
      }
      fileStream.Flush();
      fileStream.Dispose();
      Console.WriteLine("download finished");
      DownloadFinished.Invoke(null, (filename, originalURL, fullPath));
    });
    ConcurrentDownload download = new()
    {
      task = mainTask
    };
    return download;
  }

  public void StartDownload(string url, string output, IProgress<DownloadProgress> progress = null!) {
    Task.Run(async () =>
    {
      try
      {
        DownloadLinkAPIResponse response = await GetDownloadLink(url);
        ConcurrentDownload download = DownloadFile(response.URI, url, output, progress);
        downloads.Add(download);
        await download.task;
        downloads.Remove(download);
      }
      catch (Exception e) {
        Console.WriteLine($"{e.Message} : {e.StackTrace}");
      }
    });
  }
}