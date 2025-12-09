using NATS.Client.Core;

namespace NATS.Client.JetStream.Internal;

internal static class NatsJSExtensionsInternal
{
    private const string NatsPinIdHeader = "Nats-Pin-Id";

    public static long ToNanos(this TimeSpan timeSpan) => (long)(timeSpan.TotalMilliseconds * 1_000_000);

    /// <summary>
    /// Handles Pin ID mismatch (423) response by clearing the pin ID and notifying.
    /// </summary>
    /// <param name="jsConsumer">The consumer to clear the pin ID on.</param>
    /// <param name="notificationChannel">The notification channel to notify.</param>
    public static void HandlePinIdMismatch(NatsJSConsumer? jsConsumer, NatsJSNotificationChannel? notificationChannel)
    {
        jsConsumer?.SetPinId(null);
        notificationChannel?.Notify(NatsJSPinIdMismatchNotification.Default);
    }

    /// <summary>
    /// Extracts and sets the Pin ID from message headers if present.
    /// </summary>
    /// <param name="headers">The message headers.</param>
    /// <param name="jsConsumer">The consumer to set the pin ID on.</param>
    public static void TrySetPinIdFromHeaders(NatsHeaders? headers, NatsJSConsumer? jsConsumer)
    {
        if (jsConsumer != null && headers != null && headers.TryGetValue(NatsPinIdHeader, out var pinIdValues))
        {
            var pinId = pinIdValues.ToString();
            if (!string.IsNullOrEmpty(pinId))
            {
                jsConsumer.SetPinId(pinId);
            }
        }
    }

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
