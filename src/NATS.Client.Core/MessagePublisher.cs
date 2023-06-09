using System.Buffers;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

// TODO: Message publisher - cleanup
internal class MessagePublisher
{
    public static async Task PublishAsync(string subject, string? replyTo, NatsOptions? options, ReadOnlySequence<byte> buffer, object?[] callbacks)
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
                options!.LoggerFactory.CreateLogger<MessagePublisher>().LogError(ex, "Deserialize error during receive subscribed message.");
            }
            catch
            {
            }

            return;
        }

        try
        {
            if (options is { UseThreadPoolCallback: false })
            {
                foreach (var callback in callbacks!)
                {
                    if (callback != null)
                    {
                        try
                        {
                            if (callback is NatsSubBase natsSub)
                            {
                                await natsSub.ReceiveAsync(subject, replyTo, buffer).ConfigureAwait(false);
                            }
                            else if (callback is Action<ReadOnlyMemory<byte>> action)
                            {
                                action.Invoke(value);
                            }
                            else
                            {
                                throw new NatsException($"Unexpected internal handler type: {callback.GetType().Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            options!.LoggerFactory.CreateLogger<MessagePublisher>().LogError(ex, "Error occured during publish callback.");
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
                        // TODO: Message publisher - nats sub callback
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
                options!.LoggerFactory.CreateLogger<MessagePublisher>().LogError(ex, "Error occured during publish callback.");
            }
            catch
            {
            }
        }
    }
}

