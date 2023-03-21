# NATS.NET V2

## Under Development

The NATS.NET V2 client is under heavy development and is not ready for public usage.

## NATS.NET V2 Goals

- Only support Async I/O
- Target latest .NET LTS Release (currently `net6.0`)

## Packages

- **NATS.Client.Core**: [core nats](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.Hosting**: extension to configure DI container
- **NATS.Client.JetStream**: JetStream *not yet implemented*

## Contributing

- Run `dotnet format` at root directory of project in order to clear warnings that can be auto-formatted

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
