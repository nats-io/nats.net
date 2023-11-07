# NATS .NET V2

NATS.NET V2 is a [NATS](https://nats.io) client for the modern [.NET](https://dot.net/).

## Preview (Beta)

The NATS.NET V2 client is in *beta* phase and recommended for testing and feedback.
Although codebase is still under development, APIs are relatively stable and we are
looking for feedback on usability and features.

You can provide feedback:

* on [slack.nats.io dotnet channel](https://natsio.slack.com/channels/dotnet)
* or use GitHub discussions, issues and PRs

Thank you to our contributors so far. We feel we are growing slowly as a community and we appreciate your help
supporting and developing NATS .NET V2 project.

## Documentation

Check out the [documentation](https://nats-io.github.io/nats.net.v2/) for guides and examples.

**Additionally check out [NATS by example](https://natsbyexample.com) - An evolving collection of runnable, cross-client reference examples for NATS.**

## NATS.NET V2 Goals

- Only support Async I/O (async/await)
- Target latest .NET LTS Release (currently .NET 6.0, soon .NET 8.0)

## Packages

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

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
