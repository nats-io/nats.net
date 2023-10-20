using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NATS.Client.Core.Tests;

public class NuidWriterTests
{
    private static readonly Regex NuidRegex = new("[A-z0-9]{22}");

    private readonly ITestOutputHelper _outputHelper;

    public NuidWriterTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Theory]
    [InlineData(default(string))]
    [InlineData("")]
    [InlineData("__INBOX")]
    [InlineData("long-inbox-prefix-above-stackalloc-limit-of-64")]
    public void NewInbox_NuidAppended(string? prefix)
    {
        var natsOpts = NatsOpts.Default with { InboxPrefix = prefix! };
        var sut = new NatsConnection(natsOpts);

        var inbox = sut.InboxPrefix;
        var newInbox = sut.NewInbox();

        Assert.Matches($"{prefix ?? string.Empty}{(prefix?.Length > 0 ? "." : string.Empty)}[A-z0-9]{{22}}", inbox);
        Assert.Matches($"{prefix ?? string.Empty}{(prefix?.Length > 0 ? "." : string.Empty)}[A-z0-9]{{22}}.[A-z0-9]{{22}}", newInbox);
        _outputHelper.WriteLine($"Prefix:   '{prefix}'");
        _outputHelper.WriteLine($"Inbox:    '{inbox}'");
        _outputHelper.WriteLine($"NewInbox: '{newInbox}'");
    }

    [Fact]
    public void GetNextNuid_ReturnsNuidOfLength22_Char()
    {
        // Arrange
        Span<char> buffer = stackalloc char[44];

        // Act
        var result = NuidWriter.TryWriteNuid(buffer);

        // Assert
        ReadOnlySpan<char> lower = buffer.Slice(0, 22);
        string resultAsString = new(lower);
        ReadOnlySpan<char> upper = buffer.Slice(22);

        Assert.True(result);

        Assert.Matches("[A-z0-9]{22}", resultAsString);
        Assert.All(upper.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void GetNextNuid_BufferToShort_False_Char()
    {
        // Arrange
        Span<char> nuid = stackalloc char[(int)NuidWriter.NuidLength - 1];

        // Act
        var result = NuidWriter.TryWriteNuid(nuid);

        // Assert
        Assert.False(result);
        Assert.All(nuid.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void GetNextNuid_ReturnsDifferentNuidEachTime_Char()
    {
        // Arrange
        Span<char> firstNuid = stackalloc char[22];
        Span<char> secondNuid = stackalloc char[22];

        // Act
        var result = NuidWriter.TryWriteNuid(firstNuid);
        result &= NuidWriter.TryWriteNuid(secondNuid);

        // Assert
        Assert.False(firstNuid.SequenceEqual(secondNuid));
        Assert.True(result);
    }

    [Fact]
    public void GetNextNuid_PrefixIsConstant_Char()
    {
        // Arrange
        Span<char> firstNuid = stackalloc char[22];
        Span<char> secondNuid = stackalloc char[22];

        // Act
        var result = NuidWriter.TryWriteNuid(firstNuid);
        result &= NuidWriter.TryWriteNuid(secondNuid);

        // Assert
        Assert.True(result);
        Assert.True(firstNuid.Slice(0, 12).SequenceEqual(secondNuid.Slice(0, 12)));
    }

    [Fact]
    public void GetNextNuid_ContainsOnlyValidCharacters_Char()
    {
        // Arrange
        Span<char> nuid = stackalloc char[22];

        // Act
        var result = NuidWriter.TryWriteNuid(nuid);

        // Assert
        Assert.True(result);
        string resultAsString = new(nuid);
        Assert.Matches("[A-z0-9]{22}", resultAsString);
    }

    [Fact]
    public void GetNextNuid_PrefixRenewed_Char()
    {
        var result = false;
        var firstNuid = new char[22];
        var secondNuid = new char[22];

        var executionThread = new Thread(() =>
        {
            var increment = 100U;
            var maxSequential = 839299365868340224ul - increment - 1;
            SetSequentialAndIncrement(maxSequential, increment);

            result = NuidWriter.TryWriteNuid(firstNuid);
            result &= NuidWriter.TryWriteNuid(secondNuid);
        });

        executionThread.Start();
        executionThread.Join(1_000);

        // Assert
        Assert.True(result);
        Assert.False(firstNuid.AsSpan(0, 12).SequenceEqual(secondNuid.AsSpan(0, 12)));
    }

    [Fact]
    public void GetPrefix_PrefixAsExpected()
    {
        // Arrange
        var rngBytes = new byte[12] { 0, 1, 2, 3, 4, 5, 6, 7, 11, 253, 254, 255 };
        DeterministicRng rng = new(new Queue<byte[]>(new[] { rngBytes, rngBytes }));

        var mi = typeof(NuidWriter).GetMethod("GetPrefix", BindingFlags.Static | BindingFlags.NonPublic);
        var mGetPrefix = mi!.CreateDelegate<Func<RandomNumberGenerator, char[]>>();

        // Act
        var prefix = mGetPrefix(rng);

        // Assert
        Assert.Equal(12, prefix.Length);
        Assert.True("01234567B567".AsSpan().SequenceEqual(prefix));
    }

    [Fact]
    public void InitAndWrite_Char()
    {
        var completedSuccessfully = false;
        Thread t = new(() =>
        {
            var buffer = new char[22];
            var didWrite = NuidWriter.TryWriteNuid(buffer);

            var isMatch = NuidRegex.IsMatch(new string(buffer));
            Volatile.Write(ref completedSuccessfully, didWrite && isMatch);
        });
        t.Start();
        t.Join(1_000);

        Assert.True(completedSuccessfully);
    }

    [Fact]
    public void DifferentThreads_DifferentPrefixes()
    {
        // Arrange
        const int prefixLength = 12;
        ConcurrentQueue<(char[] nuid, int threadId)> nuids = new();

        // Act
        var count = 0;
        var threads = new List<Thread>();

        for (var i = 0; i < 10; i++)
        {
            Thread t = new(() =>
            {
                var buffer = new char[22];
                NuidWriter.TryWriteNuid(buffer);
                nuids.Enqueue((buffer, Environment.CurrentManagedThreadId));

                Interlocked.Increment(ref count);

                // Avoid exiting the thread so the ids won't clash.
                SpinWait.SpinUntil(() => Volatile.Read(ref count) == 10);
            });
            t.Start();
            threads.Add(t);
        }

        threads.ForEach(t => t.Join(1_000));

        // Assert
        var uniquePrefixes = new HashSet<string>();
        var uniqueThreadIds = new HashSet<int>();

        foreach (var (nuid, threadId) in nuids.ToList())
        {
            var prefix = new string(nuid.AsSpan(0, prefixLength));
            Assert.True(uniquePrefixes.Add(prefix), $"Unique prefix {prefix}");
            Assert.True(uniqueThreadIds.Add(threadId), $"Unique thread id {threadId}");
        }

        Assert.Equal(10, uniquePrefixes.Count);
        Assert.Equal(10, uniqueThreadIds.Count);
    }

    [Fact(Skip = "long running")]
    public void AllNuidsAreUnique()
    {
        const int count = 1_000 * 1_000 * 10;
        var nuids = new HashSet<string>(count);

        var buffer = new char[22];

        for (var i = 0; i < count; i++)
        {
            var didWrite = NuidWriter.TryWriteNuid(buffer);

            if (!didWrite)
            {
                Assert.Fail($"Failed to write Nuid, i: {i}");
            }

            string nuid = new(buffer);

            if (!nuids.Add(nuid))
            {
                Assert.Fail($"Duplicate Nuid: {nuid} i: {i}");
            }
        }
    }

    [Fact(Skip = "long running")]
    public void AllNuidsAreUnique_SmallSequentials()
    {
        var writeFailed = false;
        var duplicateFailure = string.Empty;
        var executionThread = new Thread(() =>
        {
            Span<char> buffer = new char[22];
            for (uint seq = 0; seq < 128; seq++)
            {
                for (uint incr = 33; incr <= 333; incr++)
                {
                    HashSet<string> nuids = new(2048);
                    SetSequentialAndIncrement(seq, incr);

                    for (var i = 0; i < 2048; i++)
                    {
                        if (!NuidWriter.TryWriteNuid(buffer))
                        {
                            writeFailed = true;
                            return;
                        }

                        var nuid = new string(buffer);

                        if (!nuids.Add(nuid))
                        {
                            duplicateFailure = $"Duplicate nuid: {nuid} seq: {seq} incr: {incr} i: {i}";
                        }
                    }
                }
            }
        });

        executionThread.Start();
        executionThread.Join(60_000);

        Interlocked.MemoryBarrier();

        Assert.False(writeFailed);
        Assert.Equal(string.Empty, duplicateFailure);
    }

    [Fact(Skip = "long running")]
    public void AllNuidsAreUnique_ZeroSequential()
    {
        var writeFailed = false;
        var duplicateFailure = string.Empty;
        var executionThread = new Thread(() =>
        {
            uint seq = 0;
            uint incr = 33;

            HashSet<string> nuids = new(2048);
            SetSequentialAndIncrement(seq, incr);

            Span<char> buffer = new char[22];
            for (var i = 0; i < 100_000_000; i++)
            {
                if (!NuidWriter.TryWriteNuid(buffer))
                {
                    writeFailed = true;
                    return;
                }

                var nuid = new string(buffer);

                if (!nuids.Add(nuid))
                {
                    duplicateFailure = $"Duplicate nuid: {nuid} seq: {seq} incr: {incr} i: {i}";
                }
            }
        });

        executionThread.Start();
        executionThread.Join(120_000);

        Interlocked.MemoryBarrier();

        Assert.False(writeFailed);
        Assert.Equal(string.Empty, duplicateFailure);
    }

    // This messes with NuidWriter's internal state and must be used
    // on separate threads (distinct NuidWriter instances) only.
    private static void SetSequentialAndIncrement(ulong sequential, ulong increment)
    {
        var didWrite = NuidWriter.TryWriteNuid(new char[128]);

        Assert.True(didWrite, "didWrite");

        var fInstance = typeof(NuidWriter).GetField("_writer", BindingFlags.Static | BindingFlags.NonPublic);
        var instance = fInstance!.GetValue(null);

        var fSequential = typeof(NuidWriter).GetField("_sequential", BindingFlags.Instance | BindingFlags.NonPublic);
        fSequential!.SetValue(instance, sequential);

        var fIncrement = typeof(NuidWriter).GetField("_increment", BindingFlags.Instance | BindingFlags.NonPublic);
        fIncrement!.SetValue(instance, increment);
    }

    private sealed class DeterministicRng : RandomNumberGenerator
    {
        private readonly Queue<byte[]> _bytes;

        public DeterministicRng(Queue<byte[]> bytes)
        {
            _bytes = bytes;
        }

        public override void GetBytes(byte[] buffer)
        {
            var nextBytes = _bytes.Dequeue();
            if (nextBytes.Length < buffer.Length)
                throw new InvalidOperationException($"Lenght of {nameof(buffer)} is {buffer.Length}, length of {nameof(nextBytes)} is {nextBytes.Length}");

            Array.Copy(nextBytes, buffer, buffer.Length);
        }
    }
}
