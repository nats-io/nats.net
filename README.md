[![License Apache 2.0](https://img.shields.io/badge/License-Apache2-blue.svg)](https://www.apache.org/licenses/LICENSE-2.0)
[![NuGet](https://img.shields.io/nuget/v/NATS.Net.svg?cacheSeconds=3600)](https://www.nuget.org/packages/NATS.Net)

# NATS.Net

[NATS](https://nats.io) client for modern [.NET](https://dot.net/).

NATS.Net v2.0 (GA) is suitable for production use.

Big thank you to our contributors.

## Documentation

Check out the [documentation](https://nats-io.github.io/nats.net.v2/) for guides and examples.

**Additionally check out [NATS by example](https://natsbyexample.com) - An evolving collection of runnable, cross-client reference examples for NATS.**

## NATS.Net Goals

- Only support Async I/O (async/await)
- Target current .NET LTS releases (currently .NET 6.0 & .NET 8.0)

## Packages

- **NATS.Net**: Meta package that includes all other packages (except serialization)
- **NATS.Client.Core**: [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.Hosting**: extension to configure DI container
- **NATS.Client.JetStream**: [JetStream](https://docs.nats.io/nats-concepts/jetstream)
- **NATS.Client.KeyValueStore**: [Key/Value Store](https://docs.nats.io/nats-concepts/jetstream/key-value-store)
- **NATS.Client.ObjectStore**: [Object Store](https://docs.nats.io/nats-concepts/jetstream/obj_store)
- **NATS.Client.Services**: [Services](https://docs.nats.io/using-nats/developer/services)
- **NATS.Client.Serializers.Json**: JSON serializer for adhoc types

## Contributing

- Run `dotnet format` at root directory of project in order to clear warnings that can be auto-formatted
- Run `dotnet build` at root directory and make sure there are no errors or warnings

Find us on [slack.nats.io dotnet channel](https://natsio.slack.com/channels/dotnet)

Please also check out the [Contributor Guide](CONTRIBUTING.md) and [Code of Conduct](CODE-OF-CONDUCT.md).

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
