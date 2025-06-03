using System.Buffers;

namespace NATS.Client.Core.Internal;

public readonly record struct NatsRecievedEvent
{
    public NatsRecievedEvent(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payload)
    {
        Subject = subject;
        ReplyTo = replyTo;
        HeadersBuffer = headersBuffer;
        Payload = payload;
    }

    public string Subject { get; }

    public string? ReplyTo { get; }

    public ReadOnlySequence<byte>? HeadersBuffer { get; }

    public ReadOnlySequence<byte> Payload { get; }
}
