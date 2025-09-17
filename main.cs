using yahd2mm;

if (args.Length == 1 && args[0].StartsWith("nxm://")) {
  await EntryPoint.RunHandler(args[0]);
}
else if (OperatingSystem.IsWindows() && args.Length == 1 && args[0] == "--fs") {
  await EntryPoint.RunWindowsSymlinker();
}
else {
  EntryPoint.RunMain();
}