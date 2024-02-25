using System.Text;

namespace NATS.Client.TestUtilities;

public static class TmpFileLogger
{
    private static readonly object Gate = new();

    public static void Log(string m)
    {
        lock (Gate)
        {
            using var fs = new FileStream("/tmp/test.log", FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            sw.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {m}");
        }
    }
}
