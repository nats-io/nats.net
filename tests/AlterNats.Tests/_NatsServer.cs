using Cysharp.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AlterNats.Tests;

public class NatsServer : IAsyncDisposable
{
    static readonly string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
    static readonly string natsServerPath = $"nats-server{ext}";

    readonly CancellationTokenSource cancellationTokenSource = new();
    readonly ITestOutputHelper outputHelper;
    readonly Task<string[]> processOut;
    readonly Task<string[]> processErr;

    public int Port { get; }

    bool isDisposed;

    public NatsServer(ITestOutputHelper outputHelper, string argument = "")
        : this(outputHelper, Random.Shared.Next(5000, 8000), argument)
    {
    }

    public NatsServer(ITestOutputHelper outputHelper, int port, string argument = "")
    {
        this.outputHelper = outputHelper;
        this.Port = port;
        var cmd = $"{natsServerPath} -p {Port} {argument}".Trim();
        outputHelper.WriteLine("ProcessStart: " + cmd);
        var (p, stdout, stderror) = ProcessX.GetDualAsyncEnumerable(cmd);

        this.processOut = EnumerteWithLogsAsync(stdout, cancellationTokenSource.Token);
        this.processErr = EnumerteWithLogsAsync(stderror, cancellationTokenSource.Token);

        // Check for start server
        Task.Run(async () =>
        {
            using var client = new TcpClient();
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await client.ConnectAsync("localhost", Port, cancellationTokenSource.Token);
                    if (client.Connected) return;
                }
                catch
                {
                    // ignore
                }

                await Task.Delay(500, cancellationTokenSource.Token);
            }
        }).Wait(5000); // timeout

        if (this.processOut.IsFaulted)
        {
            this.processOut.GetAwaiter().GetResult(); // throw exception
        }
        if (this.processErr.IsFaulted)
        {
            this.processErr.GetAwaiter().GetResult(); // throw exception
        }

        outputHelper.WriteLine("OK to Process Start, Port:" + Port);
    }

    public async ValueTask DisposeAsync()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            cancellationTokenSource.Cancel(); // trigger of process kill.
            cancellationTokenSource.Dispose();
            try
            {
                var processLogs = await processErr; // wait for process exit, nats output info to stderror
                if (processLogs.Length != 0)
                {
                    outputHelper.WriteLine("Process Logs of " + Port);
                    foreach (var item in processLogs)
                    {
                        outputHelper.WriteLine(item);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
    }

    async Task<string[]> EnumerteWithLogsAsync(ProcessAsyncEnumerable enumerable, CancellationToken cancellationToken)
    {
        var l = new List<string>();
        try
        {
            await foreach (var item in enumerable.WithCancellation(cancellationToken))
            {
                l.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
        }
        return l.ToArray();
    }

    public NatsConnection CreateClientConnection() => CreateClientConnection(NatsOptions.Default);

    public NatsConnection CreateClientConnection(NatsOptions options)
    {
        return new NatsConnection(ClientOptions(options));
    }

    public NatsConnectionPool CreatePooledClientConnection() => CreatePooledClientConnection(NatsOptions.Default);

    public NatsConnectionPool CreatePooledClientConnection(NatsOptions options)
    {
        return new NatsConnectionPool(4, ClientOptions(options));
    }

    public NatsOptions ClientOptions(NatsOptions options)
    {
        return options with
        {
            LoggerFactory = new OutputHelperLoggerFactory(outputHelper),
            //ConnectTimeout = TimeSpan.FromSeconds(1),
            //ReconnectWait = TimeSpan.Zero,
            //ReconnectJitter = TimeSpan.Zero,
            Url = $"localhost:{Port}"
        };
    }
}

public class NatsCluster : IAsyncDisposable
{
    readonly ITestOutputHelper outputHelper;

    public NatsServer Server1 { get; }
    public NatsServer Server2 { get; }
    public NatsServer Server3 { get; }

    public NatsCluster(ITestOutputHelper outputHelper)
    {
        var Port1 = Random.Shared.Next(10000, 13000);
        var Port2 = Random.Shared.Next(10000, 13000);
        var Port3 = Random.Shared.Next(10000, 13000);

        this.outputHelper = outputHelper;
        this.Server1 = new NatsServer(outputHelper, $"--cluster_name test-cluster -cluster nats://localhost:{Port1} -routes nats://localhost:{Port2},nats://localhost:{Port3}");
        this.Server2 = new NatsServer(outputHelper, $"--cluster_name test-cluster -cluster nats://localhost:{Port2} -routes nats://localhost:{Port1},nats://localhost:{Port3}");
        this.Server3 = new NatsServer(outputHelper, $"--cluster_name test-cluster -cluster nats://localhost:{Port3} -routes nats://localhost:{Port1},nats://localhost:{Port2}");
    }

    public async ValueTask DisposeAsync()
    {
        await Server1.DisposeAsync();
        await Server2.DisposeAsync();
        await Server3.DisposeAsync();
    }
}
