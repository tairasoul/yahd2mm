using System.Diagnostics;
using yahd2mm;

if (args.Length == 1 && args[0].StartsWith("nxm://")) {
  await EntryPoint.RunHandler(args[0]);
}
else {
  try
  {
    EntryPoint.RunMain();
  }
  catch (Exception ex) {
    string baseText = "yahd2mm has crashed, provide this file alongside an issue at https://github.com/tairasoul/yahd2mm/issues\n";
    baseText += ex.ToStringDemystified();
    if (!Directory.Exists(Path.Join(ModManager.yahd2mm_basepath, "crashes"))) {
      Directory.CreateDirectory(Path.Join(ModManager.yahd2mm_basepath, "crashes"));
    }
    DateTimeOffset baseOffset = new(DateTime.Now);
    File.WriteAllText(Path.Join(ModManager.yahd2mm_basepath, "crashes", $"{baseOffset:dd-MM-yyyy-HH-mm-ss}"), baseText);
    EntryPoint.OpenFile(Path.Join(ModManager.yahd2mm_basepath, "crashes", $"{baseOffset:dd-MM-yyyy-HH-mm-ss}"));
  }
}