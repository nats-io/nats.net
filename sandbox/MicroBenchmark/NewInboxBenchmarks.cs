using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace MicroBenchmark;

[MemoryDiagnoser]

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.NativeAot80)]
[SimpleJob(RuntimeMoniker.Net60)]
[SimpleJob(RuntimeMoniker.Net70, baseline: true)]
public class NewInboxBenchmarks
{
    private char[] _buf = new char[32];

    private static readonly NatsOpts LongPrefixOpt = NatsOpts.Default
        with
    {
        InboxPrefix = "this-is-a-rather-long-prefix-that-we-use-here",
    };

    private static readonly NatsConnection _connectionDefaultPrefix = new();
    private static readonly NatsConnection _connectionLongPrefix = new(LongPrefixOpt);

    [GlobalSetup]
    public void Setup()
    {
        NuidWriter.TryWriteNuid(new char[100]);
    }

    [Benchmark(Baseline = true)]
    [SkipLocalsInit]
    public bool TryWriteNuid()
    {
        return NuidWriter.TryWriteNuid(_buf);
    }

    [Benchmark]
    public string NewInbox_ShortPrefix()
    {
        return _connectionDefaultPrefix.NewInbox();
    }

    [Benchmark]
    public string NewInbox_LongPrefix()
    {
        return _connectionLongPrefix.NewInbox();
    }
}
