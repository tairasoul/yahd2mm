using System.IO.Pipes;

namespace yahd2mm;

partial class EntryPoint {
  static NamedPipeServerStream symlinker;
  public static async Task RunWindowsSymlinker() {
    symlinker = new("yahd2mmfs.pipe", PipeDirection.InOut);
    BinaryReader reader = new(symlinker);
    BinaryWriter writer = new(symlinker);
    await symlinker.WaitForConnectionAsync();
    while (true) {
      if (symlinker.IsConnected) {
        string path = reader.ReadString();
        string target = reader.ReadString();
        File.CreateSymbolicLink(path, target);
        writer.Write("Created");
        writer.Flush();
      }
    }
  }
}