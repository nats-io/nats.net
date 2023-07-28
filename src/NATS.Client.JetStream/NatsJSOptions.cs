namespace NATS.Client.JetStream;

public record NatsJSOptions
{
    public string Prefix { get; init; } = "$JS.API";
}
