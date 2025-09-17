namespace yahd2mm;

class LocalFileHolder {
  internal static string LocalFiles = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "yahd2mm_localfiles");
  public List<string> localFiles;
  FileSystemWatcher watcher;
  public LocalFileHolder() {
    if (!Directory.Exists(LocalFiles)) {
      Directory.CreateDirectory(LocalFiles);
    }
    localFiles = [.. Directory.EnumerateDirectories(LocalFiles), .. Directory.EnumerateFiles(LocalFiles)];
    SetupWatcher();
  }

  private void SetupWatcher() {
    watcher = new(LocalFiles);
    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastAccess;
    watcher.Created += (_, args) =>
    {
      if (!localFiles.Contains(args.FullPath))
        localFiles.Add(args.FullPath);
    };
    watcher.Deleted += (_, args) =>
    {
      if (localFiles.Contains(args.FullPath))
        localFiles.Remove(args.FullPath);
    };
    watcher.Renamed += (_, args) =>
    {
      if (localFiles.Contains(args.OldFullPath))
        localFiles.Remove(args.OldFullPath);
      if (!localFiles.Contains(args.FullPath))
        localFiles.Add(args.FullPath);
    };
    watcher.EnableRaisingEvents = true;
  }
}