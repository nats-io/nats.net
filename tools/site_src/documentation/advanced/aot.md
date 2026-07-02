# Native Ahead-of-Time Deployments

For [Native Ahead-of-Time (AOT) deployments](https://learn.microsoft.com/dotnet/core/deploying/native-aot),
you need to use the `NatsConnection` class directly.
This is because the `NatsClient` class uses reflection to set up the ad hoc JSON serializers, which is not supported in AOT scenarios.

If you started with the `NatsClient` class and need to switch to `NatsConnection`, you can do so without any changes to your code
because both classes implement the `INatsClient` interface.

NuGet packages that are compatible with AOT publishing are:
- [NATS.Client.Core](https://www.nuget.org/packages/NATS.Client.Core)
- [NATS.Client.JetStream](https://www.nuget.org/packages/NATS.Client.JetStream)
- [NATS.Client.KeyValueStore](https://www.nuget.org/packages/NATS.Client.KeyValueStore)
- [NATS.Client.ObjectStore](https://www.nuget.org/packages/NATS.Client.ObjectStore)
- [NATS.Client.Services](https://www.nuget.org/packages/NATS.Client.Services)
- [NATS.Client.Hosting](https://www.nuget.org/packages/NATS.Client.Hosting) (for dependency injection)

> [!NOTE]
> For dependency injection in AOT scenarios, use `NATS.Client.Hosting` (with its `AddNats()` method)
> instead of `NATS.Extensions.Microsoft.DependencyInjection`. The latter depends on `NATS.Client.Simplified` which
> includes the ad hoc JSON serializer and is not AOT-compatible.
> See [Dependency Injection](dependency-injection.md) for a full comparison of the two DI packages.
