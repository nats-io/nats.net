# NATS.NET V2

NATS.NET V2 is a [NATS](https://nats.io) client for the modern [.NET](https://dot.net/).

## Preview

The NATS.NET V2 client is in preview and not recommended for production use yet.
Codebase is still under heavy development and we currently implemented [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
and basic [JetStream](https://docs.nats.io/nats-concepts/jetstream) features.

Please test and provide feedback:

* on [slack.nats.io dotnet channel](https://natsio.slack.com/channels/dotnet)
* or use GitHub discussions, issues and PRs

Thank you to our contributors so far. We feel we are growing slowly as a community and we appreciate your help
supporting and developing NATS .NET V2 project.

## Documentation

Check out the [documentation](https://nats-io.github.io/nats.net.v2/) for guides and examples.

## NATS.NET V2 Goals

- Only support Async I/O (async/await)
- Target latest .NET LTS Release (currently .NET 6.0)

## Packages

- **NATS.Client.Core**: [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.Hosting**: extension to configure DI container
- **NATS.Client.JetStream**: [JetStream](https://docs.nats.io/nats-concepts/jetstream)

## Contributing

- Run `dotnet format` at root directory of project in order to clear warnings that can be auto-formatted

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
