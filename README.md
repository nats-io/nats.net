# NATS.Net

NATS.Net is a [NATS](https://nats.io) client for the modern [.NET](https://dot.net/).

## Release Candidate

The NATS.Net is ready to *Go Live*!

Big thank you to our contributors.

## Documentation

Check out the [documentation](https://nats-io.github.io/nats.net.v2/) for guides and examples.

**Additionally check out [NATS by example](https://natsbyexample.com) - An evolving collection of runnable, cross-client reference examples for NATS.**

## NATS.Net Goals

- Only support Async I/O (async/await)
- Target latest .NET LTS Release (currently .NET 6.0, soon .NET 8.0)

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

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
