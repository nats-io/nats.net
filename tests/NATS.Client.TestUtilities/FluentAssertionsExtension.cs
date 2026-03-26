using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions.Collections;

namespace NATS.Client.Core.Tests;

public static class FluentAssertionsExtension
{
    /// <summary>
    /// Polls <paramref name="getCollection"/> until the assertion passes or the timeout expires.
    /// Useful when an async background operation (e.g. a read-loop) populates the collection.
    /// </summary>
    public static async Task ShouldWithRetryAsync<T>(
        this Func<IReadOnlyList<T>> getCollection,
        Func<T, bool> predicate,
        string because = "",
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var timeoutValue = timeout ?? TimeSpan.FromSeconds(10);
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(100);

        using var cts = new CancellationTokenSource(timeoutValue);
        while (!cts.Token.IsCancellationRequested)
        {
            var items = getCollection();
            foreach (var item in items)
            {
                if (predicate(item))
                    return;
            }

            try
            {
                await Task.Delay(interval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Final assertion — will fail with a useful message
        getCollection().Should().Contain(item => predicate(item), because);
    }

    public static void ShouldBe(this object actual, object expected)
    {
        actual.Should().Be(expected);
    }

    public static void ShouldBeTrue(this bool v)
    {
        v.Should().BeTrue();
    }

    public static void ShouldBeFalse(this bool v)
    {
        v.Should().BeFalse();
    }

    public static void ShouldEqual(this IEnumerable<byte> source, params byte[] elements)
    {
        source.Should().Equal(elements);
    }

    public static void ShouldEqual<T>(this IEnumerable<T> source, params T[] elements)
    {
        source.Should().Equal(elements);
    }

    public static void ShouldEqual<T>(this ReadOnlyMemory<T> source, params T[] elements)
    {
        source.ToArray().Should().Equal(elements);
    }
}
