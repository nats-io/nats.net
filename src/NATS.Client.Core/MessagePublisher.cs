using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Collections.Concurrent;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

internal static class MessagePublisher
{
    // To avoid boxing, cache generic type and invoke it.
    static readonly Func<Type, PublishMessage> createPublisher = CreatePublisher;
    static readonly ConcurrentDictionary<Type, PublishMessage> publisherCache = new();

    public static void Publish(Type type, NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        publisherCache.GetOrAdd(type, createPublisher).Invoke(options, buffer, callbacks);
    }

    static PublishMessage CreatePublisher(Type type)
    {
        if (type == typeof(byte[]))
        {
            return new ByteArrayMessagePublisher().Publish;
        }
        else if (type == typeof(ReadOnlyMemory<byte>))
        {
            return new ReadOnlyMemoryMessagePublisher().Publish;
        }

        var publisher = typeof(MessagePublisher<>).MakeGenericType(type)!;
        var instance = Activator.CreateInstance(publisher)!;
        return (PublishMessage)Delegate.CreateDelegate(typeof(PublishMessage), instance, "Publish", false);
    }
}

internal delegate void PublishMessage(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks);

internal sealed class MessagePublisher<T>
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        T? value;
        try
        {
            value = options!.Serializer.Deserialize<T>(buffer);
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Deserialize error during receive subscribed message. Type:{0}", typeof(T).Name);
            }
            catch { }
            return;
        }

        try
        {
            if (!options.UseThreadPoolCallback)
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        try
                        {
                            ((Action<T?>)callback).Invoke(value);
                        }
                        catch (Exception ex)
                        {
                            options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Error occured during publish callback.");
                        }
                    }
                }
            }
            else
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        var item = ThreadPoolWorkItem<T>.Create((Action<T?>)callback, value, options!.LoggerFactory);
                        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<MessagePublisher<T>>().LogError(ex, "Error occured during publish callback.");
            }
            catch { }
        }
    }
}

internal sealed class ByteArrayMessagePublisher
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        byte[] value;
        try
        {
            if (buffer.IsEmpty)
            {
                value = Array.Empty<byte>();
            }
            else
            {
                value = buffer.ToArray();
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Deserialize error during receive subscribed message.");
            }
            catch { }
            return;
        }

        try
        {
            if (!options.UseThreadPoolCallback)
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        try
                        {
                            ((Action<byte[]?>)callback).Invoke(value);
                        }
                        catch (Exception ex)
                        {
                            options!.LoggerFactory.CreateLogger<ByteArrayMessagePublisher>().LogError(ex, "Error occured during publish callback.");
                        }
                    }
                }
            }
            else
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        var item = ThreadPoolWorkItem<byte[]>.Create((Action<byte[]?>)callback, value, options!.LoggerFactory);
                        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ByteArrayMessagePublisher>().LogError(ex, "Error occured during publish callback.");
            }
            catch { }
        }
    }
}

internal sealed class ReadOnlyMemoryMessagePublisher
{
    public void Publish(NatsOptions options, in ReadOnlySequence<byte> buffer, object?[] callbacks)
    {
        ReadOnlyMemory<byte> value;
        try
        {
            if (buffer.IsEmpty)
            {
                value = Array.Empty<byte>();
            }
            else
            {
                value = buffer.ToArray();
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Deserialize error during receive subscribed message.");
            }
            catch { }
            return;
        }

        try
        {
            if (!options.UseThreadPoolCallback)
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        try
                        {
                            ((Action<ReadOnlyMemory<byte>>)callback).Invoke(value);
                        }
                        catch (Exception ex)
                        {
                            options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Error occured during publish callback.");
                        }
                    }
                }
            }
            else
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        var item = ThreadPoolWorkItem<ReadOnlyMemory<byte>>.Create((Action<ReadOnlyMemory<byte>>)callback, value, options!.LoggerFactory);
                        ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                options!.LoggerFactory.CreateLogger<ReadOnlyMemoryMessagePublisher>().LogError(ex, "Error occured during publish callback.");
            }
            catch { }
        }
    }
}
