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
  public Func<string, string>? modifyOutput;
}

interface IFilesystemOperations {
  public void StartThread();
  public void CreateSymbolicLink(string path, string target);
  public void CreateEmpty(string path, Func<string, string>? modifyOutput = null);
  public void Copy(string from, string to, Func<string, string>? modifyOutput = null);
  public void Delete(string file);
  public void Move(string from, string to);
  public void WaitForEmpty();
}