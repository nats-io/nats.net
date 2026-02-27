# Dependency Injection

NATS .NET provides two packages for integrating with Microsoft's dependency injection framework.
We acknowledge this can be confusing — they exist because they serve slightly different use cases,
particularly around serialization defaults and AOT compatibility.

## Which Package Should I Use?

| | `NATS.Extensions.Microsoft.DependencyInjection` | `NATS.Client.Hosting` |
|---|---|---|
| **Best for** | Most applications | AOT deployments, minimal dependencies |
| **Entry method** | `AddNatsClient()` | `AddNats()` |
| **API style** | Builder pattern (fluent) | Direct parameters |
| **JSON serialization** | Enabled by default (ad hoc) | Not included |
| **AOT compatible** | No | Yes |
| **Depends on** | `NATS.Client.Simplified` (includes Core + JSON serializer) | `NATS.Client.Core` only |
| **Included in `NATS.Net`** | Yes | Yes |

**In short:**
- Use **`NATS.Extensions.Microsoft.DependencyInjection`** if you want things to "just work" with JSON serialization
  out of the box and you don't need AOT publishing.
- Use **`NATS.Client.Hosting`** if you need AOT compatibility, want minimal dependencies,
  or prefer to configure serialization yourself.

## NATS.Extensions.Microsoft.DependencyInjection

Install the package:

```shell
dotnet add package NATS.Extensions.Microsoft.DependencyInjection
```

This package provides `AddNatsClient()` with a builder pattern for configuration.
It is included in the `NATS.Net` meta-package, but can also be installed standalone.
It brings in the ad hoc JSON serializer (`NATS.Client.Serializers.Json` via `NATS.Client.Simplified`),
so you can publish and subscribe with data classes using JSON serialization without any extra setup.

### Basic Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNatsClient();
```

This registers `INatsConnection`, `NatsConnection`, and `INatsClient` in the DI container.

### Configuration

```csharp
builder.Services.AddNatsClient(nats =>
{
    nats.ConfigureOptions(opts =>
        opts.Configure(o => o.Opts = o.Opts with { Url = "nats://myserver:4222" }));
});
```

You can also configure the connection after it's created:

```csharp
builder.Services.AddNatsClient(nats =>
{
    nats.ConfigureConnection((provider, connection) =>
    {
        // Access DI services and configure the connection
    });
});
```

### Keyed Services (.NET 8+)

Register multiple NATS connections with different keys:

```csharp
builder.Services.AddNatsClient(nats =>
{
    nats.WithKey("primary");
    nats.ConfigureOptions(opts =>
        opts.Configure(o => o.Opts = o.Opts with { Url = "nats://primary:4222" }));
});

builder.Services.AddNatsClient(nats =>
{
    nats.WithKey("secondary");
    nats.ConfigureOptions(opts =>
        opts.Configure(o => o.Opts = o.Opts with { Url = "nats://secondary:4222" }));
});
```

Inject with `[FromKeyedServices]`:

```csharp
public class MyService([FromKeyedServices("primary")] INatsConnection primaryNats)
{
}
```

> [!NOTE]
> `WithKey()` must be called before `ConfigureOptions()` or other configuration methods.

## NATS.Client.Hosting

Install the package:

```shell
dotnet add package NATS.Client.Hosting
```

This package provides `AddNats()` with a simpler callback-based API.
It depends only on `NATS.Client.Core`, so it doesn't bring in the JSON serializer
or any other higher-level client packages. This makes it suitable for
[AOT deployments](aot.md) and scenarios where you want to control your dependency footprint.

### Basic Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNats();
```

This registers `INatsConnection` and `NatsConnection` in the DI container.

### Configuration

```csharp
builder.Services.AddNats(
    configureOpts: opts => opts with { Url = "nats://myserver:4222" },
    configureConnection: conn =>
    {
        // Configure the connection after creation
    });
```

### Keyed Services (.NET 8+)

```csharp
builder.Services.AddNats(key: "primary", configureOpts: opts => opts with { Url = "nats://primary:4222" });
builder.Services.AddNats(key: "secondary", configureOpts: opts => opts with { Url = "nats://secondary:4222" });
```

### Serialization

Since `NATS.Client.Hosting` does not include a JSON serializer, you need to set up serialization yourself
if you need to work with data types beyond the built-in `int`, `string`, and `byte[]` support.
See [Serialization](serialization.md) for details on configuring custom serializers,
and [AOT Deployments](aot.md) for AOT-specific guidance.

```csharp
builder.Services.AddNats(configureOpts: opts => opts with
{
    SerializerRegistry = new MyCustomSerializerRegistry(),
});
```

## Key Differences in Detail

### Serialization Defaults

`NATS.Extensions.Microsoft.DependencyInjection` automatically configures `NatsClientDefaultSerializerRegistry`,
which supports ad hoc JSON serialization using `System.Text.Json`. This means you can immediately
publish and subscribe with your own data classes:

```csharp
// Works out of the box with NATS.Extensions.Microsoft.DependencyInjection
await connection.PublishAsync("orders", new Order { Id = 1, Item = "Widget" });
```

`NATS.Client.Hosting` uses the default `NatsOpts` serializer registry, which only supports
`int`, `string`, `byte[]`, and other primitive types. For anything else, you must configure
a serializer.

### Channel Full Mode

`NATS.Extensions.Microsoft.DependencyInjection` sets `SubPendingChannelFullMode` to
`BoundedChannelFullMode.Wait` by default (matching `NatsClient` behavior). This means
a slow subscriber will apply backpressure instead of dropping messages.

`NATS.Client.Hosting` uses the `NatsOpts` default of `BoundedChannelFullMode.DropNewest`,
which drops the newest messages when the subscriber's channel is full.

### Options Pattern

`NATS.Extensions.Microsoft.DependencyInjection` uses the Microsoft Options pattern
(`IOptionsMonitor<T>`) under the hood, providing better integration with the configuration
system and support for named options.

`NATS.Client.Hosting` uses simple `Func<NatsOpts, NatsOpts>` callbacks for configuration.

## What's Next

- [Serialization](serialization.md) for configuring custom serializers.
- [AOT Deployments](aot.md) for native ahead-of-time publishing guidance.
- [Platform Compatibility](platform-compatibility.md) for API differences across target frameworks.
