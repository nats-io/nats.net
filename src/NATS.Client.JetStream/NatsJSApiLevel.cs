namespace NATS.Client.JetStream;

public readonly struct NatsJSApiLevel
{
    public const string Header = "Nats-Required-Api-Level";
    public static readonly NatsJSApiLevel None = default;
    public static readonly NatsJSApiLevel V1 = new(1);
    public static readonly NatsJSApiLevel V2 = new(2);

    private readonly int _level = 0;

    internal NatsJSApiLevel(int level) => _level = level;

    public bool IsSet() => _level > 0;

    public string GetHeaderValue() => _level.ToString();
}
