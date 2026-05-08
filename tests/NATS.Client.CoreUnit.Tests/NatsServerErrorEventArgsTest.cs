namespace NATS.Client.Core.Tests;

public class NatsServerErrorEventArgsTest
{
    // Pairs of (raw -ERR text, expected Kind). Casing matches what the
    // server actually emits; mismatches must fall through to Unknown.
    [Theory]
    [InlineData("Permissions Violation for Publish to \"foo\"", NatsServerErrorKind.PermissionsViolation)]
    [InlineData("Permissions Violation for Subscription to \"foo\"", NatsServerErrorKind.PermissionsViolation)]
    [InlineData("Permissions Violation for Subscription to \"foo\" using queue \"q\"", NatsServerErrorKind.PermissionsViolation)]
    [InlineData("Permissions Violation for Publish with Reply of \"r\"", NatsServerErrorKind.PermissionsViolation)]
    [InlineData("Authorization Violation", NatsServerErrorKind.AuthorizationViolation)]
    [InlineData("User Authentication Expired", NatsServerErrorKind.AuthenticationExpired)]
    [InlineData("User Authentication Revoked", NatsServerErrorKind.AuthenticationRevoked)]
    [InlineData("Account Authentication Expired", NatsServerErrorKind.AccountAuthenticationExpired)]
    [InlineData("Stale Connection", NatsServerErrorKind.StaleConnection)]
    [InlineData("Maximum Connections Exceeded", NatsServerErrorKind.MaximumConnectionsExceeded)]
    [InlineData("Maximum Account Active Connections Exceeded", NatsServerErrorKind.MaximumAccountConnectionsExceeded)]
    [InlineData("Maximum Subscriptions Exceeded", NatsServerErrorKind.MaximumSubscriptionsExceeded)]
    public void Recognised_kinds(string error, NatsServerErrorKind expected)
    {
        var args = new NatsServerErrorEventArgs(error);
        Assert.Equal(expected, args.Kind);
        Assert.Equal(error, args.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Authentication Timeout")]
    [InlineData("Maximum Payload Violation")]
    [InlineData("Invalid Subject")]
    [InlineData("Invalid Publish Subject")]
    [InlineData("Invalid Client Protocol")]
    [InlineData("permissions violation")]
    [InlineData("Some Future Server Error")]
    public void Unknown_kinds(string? error)
    {
        var args = new NatsServerErrorEventArgs(error!);
        Assert.Equal(NatsServerErrorKind.Unknown, args.Kind);
        Assert.Equal(error, args.Error);
    }
}
