using System.Runtime.InteropServices;

namespace yahd2mm;

class WindowsFilesystemQueue : BaseFilesystemOperations {

  [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
  private static extern bool CreateHardLink
  (
    string lpFileName,
    string lpExistingFileName,
    IntPtr lpSecurityAttributes
  );

  private static async Task<bool> WaitForFileReadyAsync(string filePath, int timeoutSeconds = 10)
  {
    TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
    DateTime startTime = DateTime.Now;

    while (DateTime.Now - startTime < timeout)
    {
      try
      {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return true;
      }
      catch (FileNotFoundException) {
        return true;
      }
      catch (IOException)
      {
        await Task.Delay(100);
      }
    }

    return false;
  }

  public override void StartThread() {
    Task.Run(async () =>
    {
      await foreach (FilesystemOperation operation in operations.Reader.ReadAllAsync())
      {
        Task task = Task.Run(async () =>
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
              if (File.Exists(operation.targets[0])) {
                await WaitForFileReadyAsync(operation.targets[0]);
                File.Delete(operation.targets[0]);
              }
              else if (Directory.Exists(operation.targets[0]))
                Directory.Delete(operation.targets[0]);
              break;
            case OperationType.Move:
              await WaitForFileReadyAsync(operation.targets[0]);
              await WaitForFileReadyAsync(operation.targets[1]);
              File.Move(operation.targets[0], operation.targets[1]);
              break;
            case OperationType.CreateSymlink:
              if (EntryPoint.UseHardlinks) 
                CreateHardLink(operation.targets[1], operation.targets[0], IntPtr.Zero);
              else
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
        await task;
      }
    });
  }
}