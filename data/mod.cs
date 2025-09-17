using Semver;

namespace yahd2mm;

struct ArsenalSubOption {
  public string Name;
  public string Description;
  public string Image;
  public string[] Include;
}

struct ArsenalOption {
  public string Name;
  public string Description;
  public string Image;
  public object[]? SubOptions;
  public string[]? Include;
}

struct ArsenalManifest {
  public int Version;
  public string Guid;
  public string Name;
  public string Description;
  public string IconPath;
  public object[]? Options;
}

struct ArsenalMod {
  public string Name;
  public readonly string Guid {
    get
    {
      if (Manifest.HasValue)
      {
        return Manifest.Value.Guid;
      }
      return Name;
    }
  }
  public SemVersion Version;
  public string FolderName;
  public ArsenalManifest? Manifest;
  public string[]? Files;
}