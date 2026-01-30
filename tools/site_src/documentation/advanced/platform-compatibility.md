# Platform Compatibility

NATS.Net targets multiple .NET platforms to provide broad compatibility:

- `netstandard2.0` - .NET Framework 4.6.1+, .NET Core 2.0+, Mono, Xamarin, Unity
- `netstandard2.1` - .NET Core 3.0+
- `net6.0` - .NET 6
- `net8.0` - .NET 8

While the API surface is designed to be consistent across all target frameworks, there are some
intentional differences due to platform capabilities. This page documents these differences.

## TLS Configuration

### SslClientAuthenticationOptions

The [`NatsTlsOpts.ConfigureClientAuthentication`](xref:NATS.Client.Core.NatsTlsOpts.ConfigureClientAuthentication)
property allows you to configure TLS client authentication options.

| Target Framework | Type |
|-----------------|------|
| `netstandard2.0` | `NATS.Client.Core.SslClientAuthenticationOptions` (polyfill) |
| `netstandard2.1`, `net6.0`, `net8.0` | `System.Net.Security.SslClientAuthenticationOptions` (BCL) |

On `netstandard2.0`, the library provides a polyfill type `NATS.Client.Core.SslClientAuthenticationOptions`
because the BCL type doesn't exist in that target framework. The polyfill provides a subset of the
properties available in the BCL type:

- `TargetHost`
- `EnabledSslProtocols`
- `ClientCertificates`
- `CertificateRevocationCheckMode`
- `RemoteCertificateValidationCallback`
- `LocalCertificateSelectionCallback`

If you need the full `SslClientAuthenticationOptions` functionality, consider targeting `netstandard2.1` or later.

## Dependency Injection

### Keyed Services

The [`AddNats`](xref:NATS.Client.Hosting.NatsHostingExtensions.AddNats*) extension method has different
signatures depending on the target framework:

**netstandard2.0, netstandard2.1, net6.0:**
```csharp
public static IServiceCollection AddNats(
    this IServiceCollection services,
    int poolSize = 1,
    Func<NatsOpts, NatsOpts>? configureOpts = null,
    Action<NatsConnection>? configureConnection = null)
```

**net8.0:**
```csharp
public static IServiceCollection AddNats(
    this IServiceCollection services,
    int poolSize = 1,
    Func<NatsOpts, NatsOpts>? configureOpts = null,
    Action<NatsConnection>? configureConnection = null,
    object? key = null)  // Additional parameter for keyed services
```

[Keyed dependency injection services](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#keyed-services)
were introduced in .NET 8. The `key` parameter allows you to register multiple NATS connections
with different keys:

```csharp
// .NET 8+ only
services.AddNats(key: "primary", configureOpts: opts => opts with { Url = "nats://primary:4222" });
services.AddNats(key: "secondary", configureOpts: opts => opts with { Url = "nats://secondary:4222" });

// Inject with [FromKeyedServices("primary")]
public class MyService([FromKeyedServices("primary")] INatsConnection primaryNats) { }
```

## API Compatibility Checking

The repository includes an API compatibility check that runs in CI to ensure APIs remain consistent
across target frameworks. Known intentional differences are documented in `apicompat.suppression.xml`
at the repository root.

To run the compatibility check locally:

```bash
./scripts/apicompat.sh --build
```

## What's Next

- [Serialization](serialization.md) is the process of converting an object into a format that can be stored or transmitted.
- [Security](security.md) is an important aspect of any distributed system. NATS provides a number of security features to help you secure your applications.
- [AOT Deployment](aot.md) is a way to deploy your applications as native platform executables.
