using Newtonsoft.Json;

namespace yahd2mm;

class ConfigData {
  public bool ActivateOptionsOnInstall;
  public bool ActivateOnInstall;
}

class Config {
  internal static string ConfigPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "config.json");
  private static ConfigData _cfg = File.Exists(ConfigPath) ? JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(ConfigPath)) : new ConfigData() { ActivateOnInstall = false, ActivateOptionsOnInstall = true };
  internal static ConfigData cfg {
    get {
      return _cfg;
    }
    set {
      _cfg = value;
      SaveConfig();
    }
  }

  internal static void SaveConfig() {
    File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_cfg));
  }
}