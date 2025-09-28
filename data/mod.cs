using Semver;

namespace yahd2mm;

struct HDMSubOption {
  public string Name;
  public string Description;
  public string Image;
  public string[] Include;
}

struct HDMOption {
  public string Name;
  public string Description;
  public string Image;
  public object[]? SubOptions;
  public string[]? Include;
}

struct BaseManifest {
  public int Version;
}

struct HDMManifestV1 {
  public int Version;
  public string Guid;
  public string Name;
  public string Description;
  public string IconPath;
  public object[]? Options;
}

struct HD2Mod {
  public string Name;
  public readonly string Guid {
    get
    {
      if (ManifestV1.HasValue)
      {
        return ManifestV1.Value.Guid;
      }
      return Name;
    }
  }
  public SemVersion Version;
  public string FolderName;
  public HDMManifestV1? ManifestV1;
  public string[]? Files;
}