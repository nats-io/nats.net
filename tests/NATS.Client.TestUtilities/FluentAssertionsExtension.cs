using System;
using System.Collections.Generic;
using FluentAssertions.Collections;

namespace NATS.Client.Core.Tests;

public static class FluentAssertionsExtension
{
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

    public static GenericCollectionAssertions<T> Should<T>(this Memory<T> source)
    {
        return source.ToArray().Should();
    }

    public static GenericCollectionAssertions<T> Should<T>(this ReadOnlyMemory<T> source)
    {
        return source.ToArray().Should();
    }

    public static GenericCollectionAssertions<T> Should<T>(this Span<T> source)
    {
        return source.ToArray().Should();
    }

    public static GenericCollectionAssertions<T> Should<T>(this ReadOnlySpan<T> source)
    {
        return source.ToArray().Should();
    }
}
