using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.ObjectPool;
using NATS.Client.Core;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;
using ObjectPool = NATS.Client.Core.Internal.ObjectPool;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace MicroBenchmark;

[MemoryDiagnoser]
[ShortRunJob]
[PlainExporter]
public class ObjectPoolBench
{
    private ObjectPool _pool1;
    private ObjectPool2<ObjectPoolEntry> _pool2;
    private Microsoft.Extensions.ObjectPool.ObjectPool<ObjectPoolEntry> _pool3;

    [Params(1, 2, 6, 8, 10, 100)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pool1 = new ObjectPool(128);
        _pool2 = new ObjectPool2<ObjectPoolEntry>(() => new ObjectPoolEntry(), 128);
        _pool3 = new DefaultObjectPool<ObjectPoolEntry>(new DefaultPooledObjectPolicy<ObjectPoolEntry>(), 128);
    }

    [Benchmark]
    public int ObjectPool1()
    {
        var t = 0;
        // var entries = new ObjectPoolEntry[Depth];

        for (var i = 0; i < Depth; i++)
        {
            if (!_pool1.TryRent<ObjectPoolEntry>(out var entry))
            {
                entry = new ObjectPoolEntry();
            }

            t += entry.Add(i);
            entries[i] = entry;
        }

        for (var index = 0; index < Depth; index++)
        {
            entries[index].Reset();
            _pool1.Return(entries[index]);
        }

        return t;
    }

    ObjectPoolEntry[] entries = new ObjectPoolEntry[128];

    [Benchmark]
    public int ObjectPool2()
    {
        var t = 0;

        for (var i = 0; i < Depth; i++)
        {
            var entry = _pool2.Get();

            t += entry.Add(i);
            entries[i] = entry;
        }

        for (var index = 0; index < Depth; index++)
        {
            entries[index].Reset();
            _pool1.Return(entries[index]);
        }

        return t;
    }

    [Benchmark]
    public int ObjectPool3()
    {
        var t = 0;

        for (var i = 0; i < Depth; i++)
        {
            var entry = _pool3.Get();

            t += entry.Add(i);
            entries[i] = entry;
        }

        for (var index = 0; index < Depth; index++)
        {
            entries[index].Reset();
            _pool3.Return(entries[index]);
        }

        return t;
    }
}

public sealed class ObjectPoolEntry : IObjectPoolNode<ObjectPoolEntry>
{
    private ObjectPoolEntry? _next;
    private DateTime _time;

    public ref ObjectPoolEntry? NextNode => ref _next;

    public int Add(int i)
    {
        _time = _time.AddSeconds(i);
        return _time.Second;
    }

    public void Reset()
    {
        _time = DateTime.MinValue;
    }
}
