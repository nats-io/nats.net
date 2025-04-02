namespace NATS.Client.Core;

/// <summary>
/// options for proxy.
/// </summary>
public sealed record NatsProxyOpts
{
    public static readonly NatsProxyOpts Default = new();

    public string? Host { get; init; }

    public int Port { get; init; }

    public string? Auth { get; init; }
}
