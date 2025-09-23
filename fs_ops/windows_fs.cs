using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace yahd2mm;

// every function here might as well be synchronous because windows is a fucking wack ass operating system and it keeps breaking
// because some other program (only god knows how or why) is using one of the mod files
class WindowsFilesystemQueue : IFilesystemOperations {
  private readonly Channel<FilesystemOperation> operations = Channel.CreateUnbounded<FilesystemOperation>();
  private readonly Lock _lock = new();
  private readonly List<Task> _inProgressTasks = [];

  [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
  private static extern bool CreateHardLink
  (
    string lpFileName,
    string lpExistingFileName,
    IntPtr lpSecurityAttributes
  );

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
              if (File.Exists(operation.targets[0]))
                File.Delete(operation.targets[0]);
              else if (Directory.Exists(operation.targets[0]))
                Directory.Delete(operation.targets[0]);
              break;
            case OperationType.Move:
              File.Move(operation.targets[0], operation.targets[1]);
              break;
            case OperationType.CreateSymlink:
              CreateHardLink(operation.modifyOutput?.Invoke(operation.targets[1]) ?? operation.targets[1], operation.targets[0], IntPtr.Zero);
              //File.Copy(operation.targets[0], operation.targets[1]);
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
    WaitForEmpty();
  }

  public void CreateEmpty(string path, Func<string, string>? modifyOutput = null) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.CreateEmpty,
      targets = [path],
      modifyOutput = modifyOutput
    });
    WaitForEmpty();
  }

  public void Copy(string from, string to, Func<string, string>? modifyOutput = null) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.Copy,
      targets = [from, to],
      modifyOutput = modifyOutput
    });
    WaitForEmpty();
  }

  public void Delete(string file) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.Delete,
      targets = [file]
    });
    WaitForEmpty();
  }

  public void Move(string from, string to) {
    operations.Writer.TryWrite(new()
    {
      type = OperationType.Move,
      targets = [from, to]
    });
    WaitForEmpty();
  }

  public void WaitForEmpty() {
    if (_inProgressTasks.Count == 0) return;
    List<Task> tasksToWaitFor;
    lock (_lock)
    {
      tasksToWaitFor = [.. _inProgressTasks];
    }
    Task.WhenAll(tasksToWaitFor).Wait();
  }
}