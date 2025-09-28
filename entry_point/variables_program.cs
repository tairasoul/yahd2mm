using Veldrid;
using Veldrid.Sdl2;

namespace yahd2mm;

partial class EntryPoint {
  public static Manager manager;
  internal static BaseFilesystemOperations queue;
  internal static LocalFileHolder files;
  internal static string HD2Path = string.Empty;
  internal static string APIKey;
  private static Sdl2Window window;
  private static GraphicsDevice gd;
  private static CommandList cl;
  private static ImGuiRenderer controller;
  internal static bool UseHardlinks;
  internal static string KeyFile = Path.Join(ModManager.yahd2mm_basepath, "key.txt");
  internal static string HD2PathFile = Path.Join(ModManager.yahd2mm_basepath, "path.txt");
  private static bool NeedsKey = !IsValidAPIKey();
  private static bool NeedsHD2DataPath;
}