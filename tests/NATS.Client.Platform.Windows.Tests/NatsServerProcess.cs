using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
#pragma warning disable SA1512
// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable NotAccessedField.Local
// ReSharper disable UseObjectOrCollectionInitializer
// ReSharper disable InconsistentNaming

namespace NATS.Client.Platform.Windows.Tests;

public class NatsServerProcess : IAsyncDisposable
{
    private readonly Action<string> _logger;
    private readonly Process _process;
    private readonly string _portsFile;

    private NatsServerProcess(Action<string> logger, Process process, string url, string portsFile)
    {
        Url = url;
        _logger = logger;
        _process = process;
        _portsFile = portsFile;
    }

    public string Url { get; }

    public static async ValueTask<NatsServerProcess> StartAsync(Action<string>? logger = null)
    {
        var log = logger ?? (_ => { });

        var tmp = Path.GetTempPath();
        var tmpEsc = tmp.Replace(@"\", @"\\");
        var info = new ProcessStartInfo
        {
            FileName = "nats-server.exe",
            Arguments = $"-a 127.0.0.1 -p -1 -js --ports_file_dir \"{tmpEsc}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var process = new Process { StartInfo = info, };

        var tcs = new TaskCompletionSource<string>();
        DataReceivedEventHandler outputHandler = (_, e) =>
        {
            log(e.Data);
            if (e.Data != null && e.Data.Contains("Server is ready"))
            {
                tcs.SetResult(e.Data);
            }
        };
        process.OutputDataReceived += outputHandler;
        process.ErrorDataReceived += outputHandler;

        process.Start();

        ChildProcessTracker.AddProcess(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await tcs.Task;

        var portsFile = Path.Combine(tmp, $"nats-server.exe_{process.Id}.ports");
        log($"portsFile={portsFile}");
        var ports = File.ReadAllText(portsFile);
        var url = Regex.Match(ports, @"nats://[\d\.]+:\d+").Groups[0].Value;
        log($"ports={ports}");
        log($"url={url}");

        return new NatsServerProcess(log, process, url, portsFile);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _process.Kill();
        }
        catch
        {
            // best effort
        }

        for (var i = 0; i < 3; i++)
        {
            try
            {
                File.Delete(_portsFile);
                break;
            }
            catch
            {
                await Task.Delay(100);
            }
        }

        _process.Dispose();
    }
}

// Borrowed from https://stackoverflow.com/questions/3342941/kill-child-process-when-parent-process-is-killed/37034966#37034966

/// <summary>
/// Allows processes to be automatically killed if this parent process unexpectedly quits.
/// This feature requires Windows 8 or greater. On Windows 7, nothing is done.</summary>
/// <remarks>References:
///  https://stackoverflow.com/a/4657392/386091
///  https://stackoverflow.com/a/9164742/386091 </remarks>
#pragma warning disable SA1204
#pragma warning disable SA1129
#pragma warning disable SA1201
#pragma warning disable SA1117
#pragma warning disable SA1400
#pragma warning disable SA1311
#pragma warning disable SA1308
#pragma warning disable SA1413
#pragma warning disable SA1121
public static class ChildProcessTracker
{
    /// <summary>
    /// Add the process to be tracked. If our current process is killed, the child processes
    /// that we are tracking will be automatically killed, too. If the child process terminates
    /// first, that's fine, too.</summary>
    /// <param name="process"></param>
    public static void AddProcess(Process process)
    {
        if (s_jobHandle != IntPtr.Zero)
        {
            bool success = AssignProcessToJobObject(s_jobHandle, process.Handle);
            if (!success && !process.HasExited)
                throw new Win32Exception();
        }
    }

    static ChildProcessTracker()
    {
        // This feature requires Windows 8 or later. To support Windows 7, requires
        //  registry settings to be added if you are using Visual Studio plus an
        //  app.manifest change.
        //  https://stackoverflow.com/a/4232259/386091
        //  https://stackoverflow.com/a/9507862/386091
        if (Environment.OSVersion.Version < new Version(6, 2))
            return;

        // The job name is optional (and can be null), but it helps with diagnostics.
        //  If it's not null, it has to be unique. Use SysInternals' Handle command-line
        //  utility: handle -a ChildProcessTracker
        string jobName = "ChildProcessTracker" + Process.GetCurrentProcess().Id;
        s_jobHandle = CreateJobObject(IntPtr.Zero, jobName);

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION();

        // This is the key flag. When our process is killed, Windows will automatically
        //  close the job handle, and when that happens, we want the child processes to
        //  be killed, too.
        info.LimitFlags = JOBOBJECTLIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        extendedInfo.BasicLimitInformation = info;

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

            if (!SetInformationJobObject(s_jobHandle, JobObjectInfoType.ExtendedLimitInformation,
                    extendedInfoPtr, (uint)length))
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string name);

    [DllImport("kernel32.dll")]
    static extern bool SetInformationJobObject(IntPtr job, JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    // Windows will automatically close any open job handles when our process terminates.
    //  This can be verified by using SysInternals' Handle utility. When the job handle
    //  is closed, the child processes will be killed.
    private static readonly IntPtr s_jobHandle;
}

public enum JobObjectInfoType
{
    AssociateCompletionPortInformation = 7,
    BasicLimitInformation = 2,
    BasicUIRestrictions = 4,
    EndOfJobTimeInformation = 6,
    ExtendedLimitInformation = 9,
    SecurityLimitInformation = 5,
    GroupInformation = 11
}

[StructLayout(LayoutKind.Sequential)]
public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    public Int64 PerProcessUserTimeLimit;
    public Int64 PerJobUserTimeLimit;
    public JOBOBJECTLIMIT LimitFlags;
    public UIntPtr MinimumWorkingSetSize;
    public UIntPtr MaximumWorkingSetSize;
    public UInt32 ActiveProcessLimit;
    public Int64 Affinity;
    public UInt32 PriorityClass;
    public UInt32 SchedulingClass;
}

[Flags]
public enum JOBOBJECTLIMIT : uint
{
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000
}

[StructLayout(LayoutKind.Sequential)]
public struct IO_COUNTERS
{
    public UInt64 ReadOperationCount;
    public UInt64 WriteOperationCount;
    public UInt64 OtherOperationCount;
    public UInt64 ReadTransferCount;
    public UInt64 WriteTransferCount;
    public UInt64 OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
{
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS IoInfo;
    public UIntPtr ProcessMemoryLimit;
    public UIntPtr JobMemoryLimit;
    public UIntPtr PeakProcessMemoryUsed;
    public UIntPtr PeakJobMemoryUsed;
}
