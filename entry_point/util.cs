using Newtonsoft.Json;
using ValveKeyValue;

namespace yahd2mm;

partial class EntryPoint {
  private static bool IsAdministrator() {
    if (OperatingSystem.IsWindows()) {
      return Environment.IsPrivilegedProcess || Path.GetPathRoot(File.ReadAllText(HD2PathFile).Trim()) == Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
    return true;
  }

  private static bool IsValidAPIKey() {
    if (!File.Exists(KeyFile)) return false;
    string apiKey = File.ReadAllText(KeyFile).Trim();
    string url = "https://api.nexusmods.com/v1/users/validate.json";
    HttpClient tempClient = new();
    tempClient.DefaultRequestHeaders.Add("apikey", apiKey);
    Task<HttpResponseMessage> httpTask = tempClient.GetAsync(url);
    httpTask.Wait();
    HttpResponseMessage message = httpTask.Result;
    return message.IsSuccessStatusCode;
  }

  public static void OpenFile(string file) {
    if (OperatingSystem.IsLinux())
      System.Diagnostics.Process.Start("xdg-open", $"\"{file}\"");
    else
      System.Diagnostics.Process.Start("explorer.exe", $"\"{file}\"");
  }

  private static bool IsValidHD2Directory(string path) {
    bool exists = Directory.Exists(path);
    bool foundBin = Directory.Exists(Path.Join(path, "..", "bin"));
    bool foundHD2 = File.Exists(Path.Join(path, "..", "bin", "helldivers2.exe"));
    return exists && foundBin && foundHD2;
  }

  struct LibraryFolder {
    public string path { get; set; }
    public Dictionary<string, string> apps { get; set; }
  }

  struct AppState {
    public string installdir { get; set; }
  }

  public static string? FindSteamLibraryFoldersVdf()
  {
    string[] possiblePaths = new[]
    {
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam", "steamapps"),
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam", "steamapps"),
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps"),
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "steamapps")
    };
    foreach (string steamappsPath in possiblePaths)
    {
      string vdfPath = Path.Combine(steamappsPath, "libraryfolders.vdf");
      if (File.Exists(vdfPath))
        return vdfPath;
    }
    return null;
  }

  private static void ScanForHD2Path() {
    if (OperatingSystem.IsLinux()) {
      string? libraryFoldersVDF = FindSteamLibraryFoldersVdf();
      if (libraryFoldersVDF == null) return;
      KVSerializer ser = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
      Dictionary<string, LibraryFolder> folders = ser.Deserialize<Dictionary<string, LibraryFolder>>(File.OpenRead(libraryFoldersVDF));
      foreach (LibraryFolder folder in folders.Values) {
        if (folder.apps.Keys.Contains("553850")) {
          string path = Path.Join(folder.path, "steamapps");
          AppState state = ser.Deserialize<AppState>(File.OpenRead(Path.Join(path, "appmanifest_553850.acf")));
          path = Path.Join(path, "common", state.installdir);
          if (Directory.Exists(path)) {
            path = Path.Join(path, "data");
            if (IsValidHD2Directory(path)) {
              HD2Path = path;
            }
          }
        }
      }
    }
    else {

    }
  }

  private static void StartManager()
  {
    APIKey = File.ReadAllText(Path.Join(ModManager.yahd2mm_basepath, "key.txt")).Trim();
    if (HD2Path == string.Empty)
      HD2Path = File.ReadAllText(Path.Join(ModManager.yahd2mm_basepath, "path.txt")).Trim();
    manager = new();
    manager.BeginListeningPipe();
  }
}