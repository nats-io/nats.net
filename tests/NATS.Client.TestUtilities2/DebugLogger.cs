namespace NATS.Client.TestUtilities2;

using System;
using System.IO;
using System.Text;

public static class DebugLogger
{
    private static readonly object Gate = new();
    private static string _filename = "/tmp/test.log";
    private static int _level = 0;
    private static Device _device = Device.None;
    private static Func<string, string> _logger = m => $"{DateTime.Now:HH:mm:ss} {m}";

    [Flags]
    public enum Device
    {
        None = 0,
        Stdout = 1,
        Stderr = 2,
        File = 3,
    }

    public static void SetFilename(string filename)
    {
        lock (Gate)
        {
            _filename = filename;
        }
    }

    public static void SetLogger(Func<string, string> logger)
    {
        lock (Gate)
        {
            _logger = logger;
        }
    }

    public static void SetDevice(Device device)
    {
        lock (Gate)
        {
            _device = device;
        }
    }

    public static void SetLevel(int level)
    {
        lock (Gate)
        {
            _level = level;
        }
    }

    public static void Log(string m, int level = 1)
    {
        lock (Gate)
        {
            if (level > _level)
                return;

            var logEntry = _logger(m);

            if (_device.HasFlag(Device.File))
            {
                using var fs = new FileStream(_filename, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs, Encoding.UTF8);
                sw.WriteLine(logEntry);
            }

            if (_device.HasFlag(Device.Stdout))
            {
                Console.Out.WriteLine(logEntry);
            }

            if (_device.HasFlag(Device.Stderr))
            {
                Console.Error.WriteLine(logEntry);
            }
        }
    }
}
