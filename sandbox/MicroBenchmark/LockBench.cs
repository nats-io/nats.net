using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace MicroBenchmark;

// Compares object lock (all TFMs) vs System.Threading.Lock (NET9+).
// On net8.0 both methods use object, so they serve as a same-TFM baseline.
// On net10.0 SystemLock uses System.Threading.Lock whose EnterScope() path
// avoids the Monitor sync-block machinery.
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
public class LockBench
{
    private readonly object _objectLock = new();

#if NET9_0_OR_GREATER
    private readonly System.Threading.Lock _systemLock = new();
#else
    private readonly object _systemLock = new();
#endif

    private int _value;

    // Uncontended empty lock/unlock -- measures raw locking overhead.
    [Benchmark(Baseline = true)]
    public void ObjectLock_Empty()
    {
        lock (_objectLock)
        {
        }
    }

    [Benchmark]
    public void SystemLock_Empty()
    {
        lock (_systemLock)
        {
        }
    }

    // Uncontended lock with a trivial increment -- closer to real usage where
    // the lock body does a small amount of work.
    [Benchmark]
    public void ObjectLock_Increment()
    {
        lock (_objectLock)
        {
            _value++;
        }
    }

    [Benchmark]
    public void SystemLock_Increment()
    {
        lock (_systemLock)
        {
            _value++;
        }
    }
}
