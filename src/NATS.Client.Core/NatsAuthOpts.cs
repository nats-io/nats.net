namespace NATS.Client.Core;

public record NatsAuthOpts
{
    public static readonly NatsAuthOpts Default = new();

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Token { get; init; }

    public string? Jwt { get; init; }

    public string? NKey { get; init; }

    public string? Seed { get; init; }

    public string? CredsFile { get; init; }

    public string? NKeyFile { get; init; }

    public bool IsAnonymous => string.IsNullOrEmpty(Username)
                               && string.IsNullOrEmpty(Password)
                               && string.IsNullOrEmpty(Token)
                               && string.IsNullOrEmpty(Jwt)
                               && string.IsNullOrEmpty(Seed)
                               && string.IsNullOrEmpty(CredsFile)
                               && string.IsNullOrEmpty(NKeyFile);
}
