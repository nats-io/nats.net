// ReSharper disable UnusedAutoPropertyAccessor.Global - properties are used by consumers outside of this library
namespace NATS.Client.Core;

public enum NatsServerErrorKind
{
    Unknown = 0,
    AuthorizationViolation,
    AuthenticationExpired,
    AuthenticationRevoked,
    AccountAuthenticationExpired,
    PermissionsViolation,
    StaleConnection,
    MaximumConnectionsExceeded,
    MaximumAccountConnectionsExceeded,
    MaximumSubscriptionsExceeded,
}

public class NatsEventArgs : EventArgs
{
    public NatsEventArgs(string message) => Message = message;

    public string Message { get; }
}

public class NatsMessageDroppedEventArgs : NatsEventArgs
{
    public NatsMessageDroppedEventArgs(NatsSubBase subscription, int pending, string subject, string? replyTo, NatsHeaders? headers, object? data)
        : base($"Dropped message from {subject} with {pending} pending messages")
    {
        Subscription = subscription;
        Pending = pending;
        Subject = subject;
        ReplyTo = replyTo;
        Headers = headers;
        Data = data;
    }

    public NatsSubBase Subscription { get; }

    public int Pending { get; }

    public string Subject { get; }

    public string? ReplyTo { get; }

    public NatsHeaders? Headers { get; }

    public object? Data { get; }
}

public class NatsLameDuckModeActivatedEventArgs : NatsEventArgs
{
    public NatsLameDuckModeActivatedEventArgs(Uri uri)
        : base("Lame duck mode activated") => Uri = uri;

    public Uri Uri { get; }
}

public class NatsSlowConsumerEventArgs : NatsEventArgs
{
    public NatsSlowConsumerEventArgs(NatsSubBase subscription)
        : base($"Slow consumer detected on subscription {subscription.Subject}")
    {
        Subscription = subscription;
    }

    public NatsSubBase Subscription { get; }
}

public class NatsServerErrorEventArgs : NatsEventArgs
{
    public NatsServerErrorEventArgs(string error)
        : base($"Server error {error}")
    {
        Error = error;
        Kind = ParseKind(error);
    }

    public string Error { get; }

    public NatsServerErrorKind Kind { get; }

    // Match the literal -ERR strings the server emits to clients (see
    // nats-server/server/client.go and errors.go). Server casing is fixed
    // in source, so Ordinal is enough. Anything unrecognised stays as
    // Unknown; the raw string is always available on Error.
    private static NatsServerErrorKind ParseKind(string error)
    {
        if (string.IsNullOrEmpty(error))
            return NatsServerErrorKind.Unknown;

        if (error.StartsWith("Permissions Violation", StringComparison.Ordinal))
            return NatsServerErrorKind.PermissionsViolation;
        if (error.StartsWith("Authorization Violation", StringComparison.Ordinal))
            return NatsServerErrorKind.AuthorizationViolation;
        if (error.StartsWith("User Authentication Expired", StringComparison.Ordinal))
            return NatsServerErrorKind.AuthenticationExpired;
        if (error.StartsWith("User Authentication Revoked", StringComparison.Ordinal))
            return NatsServerErrorKind.AuthenticationRevoked;
        if (error.StartsWith("Account Authentication Expired", StringComparison.Ordinal))
            return NatsServerErrorKind.AccountAuthenticationExpired;
        if (error.StartsWith("Stale Connection", StringComparison.Ordinal))
            return NatsServerErrorKind.StaleConnection;
        if (error.StartsWith("Maximum Subscriptions", StringComparison.Ordinal))
            return NatsServerErrorKind.MaximumSubscriptionsExceeded;
        if (error.StartsWith("Maximum Account Active Connections", StringComparison.Ordinal))
            return NatsServerErrorKind.MaximumAccountConnectionsExceeded;
        if (error.StartsWith("Maximum Connections", StringComparison.Ordinal))
            return NatsServerErrorKind.MaximumConnectionsExceeded;

        return NatsServerErrorKind.Unknown;
    }
}
