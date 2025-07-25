using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace NATS.Client.TestUtilities;

public class NatsServerExe
{
    public static readonly Version Version;
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string Ext = IsWindows ? ".exe" : string.Empty;
    private static readonly string NatsServerPath = $"nats-server{Ext}";

    static NatsServerExe()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = NatsServerPath,
                Arguments = "-v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        process.Start();
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var value = Regex.Match(output, @"v(\d+\.\d+\.\d+)").Groups[1].Value;
        Version = new Version(value);
    }

    public static bool SupportsTlsFirst() => new Version("2.10.4") <= Version;
}

#pragma warning disable SA1204
public static class ServerVersions
#pragma warning restore SA1204
{
#pragma warning disable SA1310
#pragma warning disable SA1401

    // Changed INFO port reporting for WS connections (nats-server #4255)
    public static Version V2_9_19 = new("2.9.19");

#pragma warning restore SA1401
#pragma warning restore SA1310
}

public class NullOutputHelper : ITestOutputHelper
{
    public string Output => string.Empty;

    public void Write(string message)
    {
    }

    public void Write(string format, params object[] args)
    {
    }

    public void WriteLine(string message)
    {
    }

    public void WriteLine(string format, params object[] args)
    {
    }
}

public sealed class SkipIfNatsServer : FactAttribute
{
    private static readonly bool SupportsTlsFirst;

    static SkipIfNatsServer() => SupportsTlsFirst = NatsServerExe.SupportsTlsFirst();

    public SkipIfNatsServer(bool doesNotSupportTlsFirst = false, string? versionEarlierThan = default, string? versionLaterThan = default)
    {
        if (doesNotSupportTlsFirst && !SupportsTlsFirst)
        {
            Skip = "NATS server doesn't support TLS first";
        }

        if (versionEarlierThan != null && new Version(versionEarlierThan) > NatsServerExe.Version)
        {
            Skip = $"NATS server version ({NatsServerExe.Version}) is earlier than {versionEarlierThan}";
        }

        if (versionLaterThan != null && new Version(versionLaterThan) < NatsServerExe.Version)
        {
            Skip = $"NATS server version ({NatsServerExe.Version}) is later than {versionLaterThan}";
        }
    }
}

public sealed class SkipIfNatsServerTheory : TheoryAttribute
{
    private static readonly bool SupportsTlsFirst;

    static SkipIfNatsServerTheory() => SupportsTlsFirst = NatsServerExe.SupportsTlsFirst();

    public SkipIfNatsServerTheory(bool doesNotSupportTlsFirst = false, string? versionEarlierThan = default, string? versionLaterThan = default)
    {
        if (doesNotSupportTlsFirst && !SupportsTlsFirst)
        {
            Skip = "NATS server doesn't support TLS first";
        }

        if (versionEarlierThan != null && new Version(versionEarlierThan) > NatsServerExe.Version)
        {
            Skip = $"NATS server version ({NatsServerExe.Version}) is earlier than {versionEarlierThan}";
        }

        if (versionLaterThan != null && new Version(versionLaterThan) < NatsServerExe.Version)
        {
            Skip = $"NATS server version ({NatsServerExe.Version}) is later than {versionLaterThan}";
        }
    }
}

public sealed class SkipOnPlatform : FactAttribute
{
    public SkipOnPlatform(string platform, string reason)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Create(platform)))
        {
            Skip = $"Platform {platform} is not supported: {reason}";
        }
    }
}
