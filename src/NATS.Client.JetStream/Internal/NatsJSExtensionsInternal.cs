using NATS.Client.Core;

namespace NATS.Client.JetStream.Internal;

public static class NatsJSExtensionsInternal
{
    public static long ToNanos(this TimeSpan timeSpan) => (long)(timeSpan.TotalMilliseconds * 1_000_000);

    public static bool HasTerminalJSError(this NatsHeaders headers) => headers
        is { Code: 400 }
        or { Code: 409, Message: NatsHeaders.Messages.ConsumerDeleted }
        or { Code: 409, Message: NatsHeaders.Messages.ConsumerIsPushBased };
}
