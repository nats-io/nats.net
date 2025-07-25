using NATS.Client.Core;

namespace NATS.Client.JetStream.Internal;

internal static class NatsJSExtensionsInternal
{
    public static long ToNanos(this TimeSpan timeSpan) => (long)(timeSpan.TotalMilliseconds * 1_000_000);

    public static bool HasTerminalJSError(this NatsHeaders headers)
    {
        // terminal codes
        if (headers is { Code: 400 } or { Code: 404 })
            return true;

        // sometimes terminal 409s
        if (headers is { Code: 409 })
        {
            if (headers is { Message: NatsHeaders.Messages.ConsumerDeleted } or { Message: NatsHeaders.Messages.ConsumerIsPushBased })
                return true;

            if (headers.MessageText.StartsWith("Exceeded MaxRequestBatch", StringComparison.OrdinalIgnoreCase)
                || headers.MessageText.StartsWith("Exceeded MaxRequestExpires", StringComparison.OrdinalIgnoreCase)
                || headers.MessageText.StartsWith("Exceeded MaxRequestMaxBytes", StringComparison.OrdinalIgnoreCase)
                || headers.MessageText.StartsWith("Consumer is push based", StringComparison.OrdinalIgnoreCase)
                || headers.MessageText.StartsWith("Exceeded MaxWaiting", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
