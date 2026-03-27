# Security

NATS has a lot of [security features](https://docs.nats.io/nats-concepts/security) and .NET V2 client supports them all.
All you need to do is to pass your credentials to the connection.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SecurityPage.cs#user-pass)]

See also [user authentication tests](https://github.com/nats-io/nats.net/blob/main/tests/NATS.Client.Core.Tests/NatsConnectionTest.Auth.cs) for more examples.

## Implicit TLS Connections

As of NATS server version 2.10.4 and later, the server supports implicit TLS connections.
This means that the client can connect to the server using the default port of 4222 and the server will automatically upgrade the connection to TLS.
This is useful for environments where TLS is required by default.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SecurityPage.cs#tls-implicit)]

## Mutual TLS Connections

The [server can require TLS certificates from a client](https://docs.nats.io/running-a-nats-service/configuration/securing_nats/auth_intro/tls_mutual_auth) to validate
the client certificate matches a known or trusted CA and to provide authentication.

You can set the TLS options to use your client certificates when connecting to a server which requires TLS Mutual authentication.

[!code-csharp[](../../../../tests/NATS.Net.DocsExamples/Advanced/SecurityPage.cs#tls-mutual)]

> [!TIP]
> #### Intermediate CA Certificates
>
> When connecting using intermediate CA certificates, it might not be possible to validate the client certificate and
> TLS handshake may fail.
>
> Unfortunately, for .NET client applications it isn't possible to pass additional intermediate certificates and the
> only solution is to add the certificates to the certificate store manually.
>
> See also .NET documentation on [Troubleshooting SslStream authentication issues](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting#intermediate-certificates-are-not-sent)

## TLS Modes

The .NET client supports several TLS modes via `NatsTlsOpts.Mode`:

| Mode | Behavior |
|------|----------|
| `Auto` (default) | Resolves to `Prefer` for `nats://` without certificates, `Require` for `tls://` or when certificates are provided |
| `Prefer` | Upgrades to TLS if the server advertises TLS support, otherwise connects in plaintext |
| `Require` | Always requires TLS, fails if the server does not support it |
| `Implicit` | Connects with TLS immediately, before any protocol exchange |
| `Disable` | Never attempts TLS, always connects in plaintext |

### TLS Behind a Proxy

When the nats-server is behind a TLS-terminating proxy, the server may advertise TLS support
(`tls_available`) even though TLS is handled by the proxy. In this configuration the default
`Auto`/`Prefer` mode will attempt a TLS upgrade that the nats-server cannot complete, causing
the connection to fail.

Set `TlsMode.Disable` to skip the TLS upgrade:

```csharp
var opts = new NatsOpts
{
    Url = "nats://my-nats-behind-proxy:4222",
    TlsOpts = new NatsTlsOpts { Mode = TlsMode.Disable },
};
await using var nats = new NatsConnection(opts);
```

> [!NOTE]
> This behavior differs from most other NATS clients, which do not attempt a TLS upgrade
> when the server only advertises `tls_available` without `tls_required`.
