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
