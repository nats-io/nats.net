using System.Buffers;
using System.Text;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal static class CommandWriterExtensions
{
    public static ValueTask ConnectAsync(this ICommandWriter commandWriter, ClientOpts connectOpts, CancellationToken cancellationToken) =>
        commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WriteConnect(connectOpts);
            },
            cancellationToken);

    public static ValueTask DirectWriteAsync(this ICommandWriter commandWriter, string protocol, int repeatCount, CancellationToken cancellationToken)
    {
        if (repeatCount < 1)
            throw new ArgumentException("repeatCount should >= 1, repeatCount:" + repeatCount);

        byte[] protocolBytes;
        if (repeatCount == 1)
        {
            protocolBytes = Encoding.UTF8.GetBytes(protocol + "\r\n");
        }
        else
        {
            var bin = Encoding.UTF8.GetBytes(protocol + "\r\n");
            protocolBytes = new byte[bin.Length * repeatCount];
            var span = protocolBytes.AsSpan();
            for (var i = 0; i < repeatCount; i++)
            {
                bin.CopyTo(span);
                span = span.Slice(bin.Length);
            }
        }

        return commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WriteRaw(protocolBytes);
            },
            cancellationToken);
    }

    public static ValueTask PingAsync(this ICommandWriter commandWriter, CancellationToken cancellationToken) =>
        commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WritePing();
            },
            cancellationToken);

    public static ValueTask PongAsync(this ICommandWriter commandWriter, CancellationToken cancellationToken) =>
        commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WritePong();
            },
            cancellationToken);

    public static ValueTask PublishAsync<T>(this ICommandWriter commandWriter, string subject, string? replyTo, NatsHeaders? headers, T? value, INatsSerialize<T> serializer, CancellationToken cancellationToken) =>
        commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WritePublish(subject, replyTo, headers, value, serializer);
            },
            cancellationToken);

    public static ValueTask PublishBytesAsync(this ICommandWriter commandWriter, string subject, string? replyTo, NatsHeaders? headers, ReadOnlySequence<byte> payload, CancellationToken cancellationToken) =>
        commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WritePublish(subject, replyTo, headers, payload);
            },
            cancellationToken);

    public static ValueTask SubscribeAsync(this ICommandWriter commandWriter, int sid, string subject, string? queueGroup, int? maxMsgs, CancellationToken cancellationToken) =>
        commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WriteSubscribe(sid, subject, queueGroup, maxMsgs);
            },
            cancellationToken);

    public static ValueTask UnsubscribeAsync(this ICommandWriter commandWriter, int sid, CancellationToken cancellationToken) =>
        commandWriter.WriteCommandAsync(
            writer =>
            {
                writer.WriteUnsubscribe(sid, null);
            },
            cancellationToken);
}
