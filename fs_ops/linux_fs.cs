namespace yahd2mm;

class LinuxFilesystemQueue : BaseFilesystemOperations {
  public override void StartThread() {
    Task.Run(async () =>
    {
      await foreach (var operation in operations.Reader.ReadAllAsync())
      {
        var task = Task.Run(() =>
        {
          switch (operation.type)
          {
            case OperationType.Copy:
              File.Copy(operation.targets[0], operation.targets[1]);
              break;
            case OperationType.CreateEmpty:
              using (File.Create(operation.targets[0]))
              { }
              break;
            case OperationType.Delete:
              if (File.Exists(operation.targets[0]))
                File.Delete(operation.targets[0]);
              else if (Directory.Exists(operation.targets[0]))
                Directory.Delete(operation.targets[0]);
              break;
            case OperationType.Move:
              File.Move(operation.targets[0], operation.targets[1]);
              break;
            case OperationType.CreateSymlink:
              File.CreateSymbolicLink(operation.targets[1], operation.targets[0]);
              break;
          }
        });

        lock (_lock)
        {
          _inProgressTasks.Add(task);
        }
        task.ContinueWith(_ =>
        {
          lock (_lock)
          {
            _inProgressTasks.Remove(task);
          }
        });
      }
    });
  }
}