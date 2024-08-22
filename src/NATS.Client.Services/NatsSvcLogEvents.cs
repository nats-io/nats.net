using Microsoft.Extensions.Logging;

namespace NATS.Client.Services;

public static class NatsSvcLogEvents
{
    public static readonly EventId Endpoint = new(5001, nameof(Endpoint));
    public static readonly EventId Listener = new(5002, nameof(Listener));
}
