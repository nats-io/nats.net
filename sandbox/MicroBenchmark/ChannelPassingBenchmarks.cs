using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace MicroBenchmark;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class ChannelPassingBenchmarks
{
    private NatsConnection Connection;
    private SubWrappedChannel<string> _inFlightNatsMsgChannel;
    private Channel<NatsMsg<string>> _natsMsgChannel;
    private BoundedChannelOptions ChannelOpts;
    private CancellationTokenSource _cts;

    [GlobalCleanup]
    public void TearDown()
    {
        _cts.Dispose();
        _natsMsgChannel = null;
        _inFlightNatsMsgChannel = null;
    }

    [GlobalSetup]
    public void Setup()
    {
        Connection = new NatsConnection();
        ChannelOpts = Connection.GetChannelOpts(Connection.Opts, default);
        _inFlightNatsMsgChannel = new SubWrappedChannel<string>(
            Channel.CreateBounded<InFlightNatsMsg<string>>(
                ChannelOpts),
            Connection);
        _natsMsgChannel = Channel.CreateBounded<NatsMsg<string>>(
            ChannelOpts);
        _cts = new CancellationTokenSource();
    }

    [Benchmark]
    public void RunNatsMsgChannel_Sync()
    {
        var maxCount = ChannelOpts.Capacity;


        for (int i = 0; i < maxCount; i++)
        {
            _natsMsgChannel.Writer.TryWrite(new NatsMsg<string>("t", default, 3, default, "foo", default));
        }

        var reader = _natsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            reader.TryRead(out _);
        }
    }

    [Benchmark]
    public void RunInFlightNatsMsgChannel_Sync()
    {
        var maxCount = ChannelOpts.Capacity;

        var writer = _inFlightNatsMsgChannel.Writer;
        for (int i = 0; i < maxCount; i++)
        {
            writer.TryWrite(new InFlightNatsMsg<string>("t", default, 3, default, "foo"));
        }

        var reader = _inFlightNatsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            reader.TryRead(out _);
        }
    }

    [Benchmark]
    public void RunNatsMsgChannel_Sync_Pulse()
    {
        var maxCount = ChannelOpts.Capacity;


        var reader = _natsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            _natsMsgChannel.Writer.TryWrite(new NatsMsg<string>("t", default, 3, default, "foo", default));
            reader.TryRead(out _);
        }
    }

    [Benchmark]
    public void RunInFlightNatsMsgChannel_Sync_Pulse()
    {
        var maxCount = ChannelOpts.Capacity;

        var writer = _inFlightNatsMsgChannel.Writer;
        var reader = _inFlightNatsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            writer.TryWrite(new InFlightNatsMsg<string>("t", default, 3, default, "foo"));
            reader.TryRead(out _);
        }
    }

    [Benchmark]
    public async Task RunNatsMsgChannel_Async_Pulse()
    {
        var maxCount = ChannelOpts.Capacity;


        var reader = _natsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            _natsMsgChannel.Writer.TryWrite(new NatsMsg<string>("t", default, 3, default, "foo", default));
            await reader.ReadAsync(_cts.Token);
        }
    }

    [Benchmark]
    public async Task RunInFlightNatsMsgChannel_Async_Pulse()
    {
        var maxCount = ChannelOpts.Capacity;

        var writer = _inFlightNatsMsgChannel.Writer;
        var reader = _inFlightNatsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            writer.TryWrite(new InFlightNatsMsg<string>("t", default, 3, default, "foo"));
            await reader.ReadAsync(_cts.Token);
        }
    }

    [Benchmark]
    public async Task RunNatsMsgChannel_Async_Pulse_Unhappy()
    {
        var maxCount = ChannelOpts.Capacity;

        var writer = _natsMsgChannel.Writer;
        var reader = _natsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            var r = reader.ReadAsync(_cts.Token);
            writer.TryWrite(new NatsMsg<string>("t", default, 3, default, "foo", default));
            var v = await r;
            if (v.Subject == null)
            {
                Console.WriteLine("wat");
            }
        }
    }

    [Benchmark]
    public async Task RunInFlightNatsMsgChannel_Async_Pulse_Unhappy()
    {
        var maxCount = ChannelOpts.Capacity;

        var writer = _inFlightNatsMsgChannel.Writer;
        var reader = _inFlightNatsMsgChannel.Reader;
        for (int i = 0; i < maxCount; i++)
        {
            var r = reader.ReadAsync(_cts.Token);
            writer.TryWrite(new InFlightNatsMsg<string>("t", default, 3, default, "foo"));
            var v = await r;
            if (v.Subject == null)
            {
                Console.WriteLine("wat");
            }
        }
    }

    [Benchmark]
    public async Task RunNatsMsgChannel_Async_Unhappy()
    {
        var maxCount = ChannelOpts.Capacity;

        var readTask = Task.Run(
            async () =>
            {
                var reader = _natsMsgChannel.Reader;
                for (int i = 0; i < maxCount; i++)
                {
                    await reader.ReadAsync(_cts.Token);
                }
            });
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            () =>
            {
                for (int i = 0; i < maxCount; i++)
                {
                    _natsMsgChannel.Writer.TryWrite(new NatsMsg<string>("t", default, 3, default, "foo", default));
                }
            });
        await readTask;
    }

    [Benchmark]
    public async Task RunInFlightNatsMsgChannel_Async_Unhappy()
    {
        var maxCount = ChannelOpts.Capacity;

        var readTask = Task.Run(
            async () =>
            {
                var reader = _inFlightNatsMsgChannel.Reader;
                for (int i = 0; i < maxCount; i++)
                {
                    await reader.ReadAsync(_cts.Token);
                }
            });

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            () =>
            {
                var writer = _inFlightNatsMsgChannel.Writer;
                for (int i = 0; i < maxCount; i++)
                {
                    writer.TryWrite(new InFlightNatsMsg<string>("t", default, 3, default, "foo"));
                }
            });
        await readTask;
    }

    [Benchmark]
    public async Task RunNatsMsgChannel_Async_Happy()
    {
        var maxCount = ChannelOpts.Capacity;

        for (int i = 0; i < maxCount; i++)
        {
            _natsMsgChannel.Writer.TryWrite(new NatsMsg<string>("t", default, 3, default, "foo", default));
        }

        var readTask = Task.Run(
            async () =>
            {
                var reader = _natsMsgChannel.Reader;
                for (int i = 0; i < maxCount; i++)
                {
                    await reader.ReadAsync(_cts.Token);
                }
            });

        await readTask;
    }

    [Benchmark]
    public async Task RunInFlightNatsMsgChannel_Async_Happy()
    {
        var maxCount = ChannelOpts.Capacity;

        var writer = _inFlightNatsMsgChannel.Writer;
        for (int i = 0; i < maxCount; i++)
        {
            writer.TryWrite(new InFlightNatsMsg<string>("t", default, 3, default, "foo"));
        }

        var readTask = Task.Run(
            async () =>
            {
                var reader = _inFlightNatsMsgChannel.Reader;
                for (int i = 0; i < maxCount; i++)
                {
                    await reader.ReadAsync(_cts.Token);
                }
            });
        await readTask;
    }
}
