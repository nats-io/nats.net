namespace NATS.Client.JetStream.Internal;

public static class NatsJSExtensionsInternal
{
    public static long ToNanos(this TimeSpan timeSpan) => (long)(timeSpan.TotalMilliseconds * 1_000_000);
}
