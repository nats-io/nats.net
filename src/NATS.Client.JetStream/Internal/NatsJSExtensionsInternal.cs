namespace NATS.Client.JetStream.Internal;

public static class NatsJSExtensionsInternal
{
    internal static long ToNanos(this TimeSpan timeSpan) => (long)(timeSpan.TotalMilliseconds * 1_000_000);
}
