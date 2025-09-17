using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace yahd2mm;

partial class EntryPoint {
  private static bool ProcessStarted = false;
  public static async Task RunHandler(string nxm_url) {
    NamedPipeClientStream client = new(".", "yahd2mm.pipe", PipeDirection.Out);
    try
    {
      client.Connect(2);
      using StreamWriter writer = new(client, Encoding.UTF8);
      writer.WriteLine(nxm_url);
      writer.Flush();
    }
    catch (TimeoutException) {
      if (!ProcessStarted)
      {
        ProcessStartInfo start = new()
        {
          FileName = Environment.ProcessPath,
          UseShellExecute = false,
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Normal
        };
        Process.Start(start);
        ProcessStarted = true;
      }
      await Task.Delay(1000);
      await RunHandler(nxm_url);
    }
  }
}