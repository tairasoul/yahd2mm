using Newtonsoft.Json;

namespace yahd2mm;

struct Modpack {
  public string Name;
  public ModpackMod[] mods;
}

struct ModpackMod {
  public string name;
  public string guid;
  public string[]? options;
}

class ModpackManager {
  internal static readonly string Modpacks = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "modpacks.json");
  internal Dictionary<string, Modpack> modpacks = File.Exists(Modpacks) ? JsonConvert.DeserializeObject<Dictionary<string, Modpack>>(File.ReadAllText(Modpacks)) ?? [] : [];
  public void SaveData() {
    File.WriteAllText(Modpacks, JsonConvert.SerializeObject(modpacks));
  }

  private string GetNewGUID() {
    string guid = Guid.NewGuid().ToString();
    if (modpacks.ContainsKey(guid)) return GetNewGUID();
    return guid;
  }

  public string CreateModpack(string name) {
    string GUID = GetNewGUID();
    modpacks[GUID] = new() {
      mods = [],
      Name = name
    };
    SaveData();
    return GUID;
  }

  public void DeleteModpack(string name) {
    modpacks.Remove(name);
    SaveData();
  }

  public void AddModToModpack(string name, string guid, string modpack, string[]? options) {
    if (!modpacks.TryGetValue(modpack, out Modpack pack)) return;
    ModpackMod added = new()
    {
      name = name,
      guid = guid,
      options = options
    };
    modpacks[modpack] = pack with { mods = [.. pack.mods, added] };
    SaveData();
  }

  public void RemoveModFromModpack(string name, string modpack) {
    if (!modpacks.TryGetValue(modpack, out Modpack pack)) return;
    modpacks[modpack] = pack with { mods = pack.mods.Where((v) => v.guid != name).ToArray() } ;
    SaveData();
  }

  public void LoadModpack(string name, ModManager manager) {
    if (!modpacks.TryGetValue(name, out Modpack pack)) return;
    foreach (KeyValuePair<string, ModJson> mod in manager.modState) {
      if (mod.Value.Enabled) {
        manager.DisableMod(mod.Key);
      }
    }
    foreach (ModpackMod mod in pack.mods) {
      manager.EnableMod(mod.guid);
      if (mod.options != null)
        foreach (string option in mod.options) {
          manager.EnableChoice(mod.guid, option);
        }
    }
  }
}