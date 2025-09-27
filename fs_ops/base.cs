using System.Threading.Channels;

namespace yahd2mm;

enum OperationType {
  Copy,
  Delete,
  Move,
  CreateEmpty,
  CreateSymlink
}

struct FilesystemOperation {
  public OperationType type;
  public string[] targets;
}

abstract class BaseFilesystemOperations {
  internal readonly Channel<FilesystemOperation> operations = Channel.CreateUnbounded<FilesystemOperation>();
  internal readonly Lock _lock = new();
  internal readonly List<Task> _inProgressTasks = [];
  public abstract void StartThread();

  public void CreateSymbolicLink(string path, string target) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.CreateSymlink,
      targets = [path, target]
    });
  }

  public void CreateEmpty(string path) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.CreateEmpty,
      targets = [path]
    });
  }

  public void Copy(string from, string to) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.Copy,
      targets = [from, to]
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
      tasksToWaitFor = [.. _inProgressTasks];
    }
    Task.WhenAll(tasksToWaitFor).Wait();
  }
}