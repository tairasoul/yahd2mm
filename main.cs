using yahd2mm;

if (args.Length == 1 && args[0].StartsWith("nxm://")) {
  await EntryPoint.RunHandler(args[0]);
}
else {
  EntryPoint.RunMain();
}