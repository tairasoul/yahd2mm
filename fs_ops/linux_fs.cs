using System.Threading.Channels;

namespace yahd2mm;

class FilesystemQueue : IFilesystemOperations {
  private Channel<FilesystemOperation> operations = Channel.CreateUnbounded<FilesystemOperation>();
  private readonly Lock _lock = new();
  private readonly List<Task> _inProgressTasks = new();

  public void StartThread() {
    Task.Run(async () =>
    {
      await foreach (var operation in operations.Reader.ReadAllAsync())
      {
        var task = Task.Run(() =>
        {
          switch (operation.type)
          {
            case OperationType.Copy:
              File.Copy(operation.targets[0], operation.modifyOutput?.Invoke(operation.targets[1]) ?? operation.targets[1]);
              break;
            case OperationType.CreateEmpty:
              using (File.Create(operation.modifyOutput?.Invoke(operation.targets[0]) ?? operation.targets[0]))
              { }
              break;
            case OperationType.Delete:
              File.Delete(operation.targets[0]);
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

  public void CreateSymbolicLink(string path, string target) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.CreateSymlink,
      targets = [path, target]
    });
  }

  public void CreateEmpty(string path, Func<string, string>? modifyOutput = null) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.CreateEmpty,
      targets = [path],
      modifyOutput = modifyOutput
    });
  }

  public void Copy(string from, string to, Func<string, string>? modifyOutput = null) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.Copy,
      targets = [from, to],
      modifyOutput = modifyOutput
    });
  }

  public void Delete(string file) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.Delete,
      targets = [file]
    });
  }

  public void Move(string from, string to) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.Move,
      targets = [from, to]
    });
  }

  public void WaitForEmpty() {
    List<Task> tasksToWaitFor;
    lock (_lock)
    {
      tasksToWaitFor = new List<Task>(_inProgressTasks);
    }
    Task.WhenAll(tasksToWaitFor).Wait();
  }
}