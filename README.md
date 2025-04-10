[![License Apache 2.0](https://img.shields.io/badge/License-Apache2-blue.svg)](https://www.apache.org/licenses/LICENSE-2.0)
[![NuGet](https://img.shields.io/nuget/v/NATS.Net.svg?cacheSeconds=3600)](https://www.nuget.org/packages/NATS.Net)
[![Doc](https://img.shields.io/badge/Doc-reference-blue)](https://nats-io.github.io/nats.net/)
[![Test Linux](https://github.com/nats-io/nats.net/actions/workflows/test_linux.yml/badge.svg?branch=main)](https://github.com/nats-io/nats.net/actions/workflows/test_linux.yml?query=branch%3Amain)
[![Test Windows](https://github.com/nats-io/nats.net/actions/workflows/test_windows.yml/badge.svg?branch=main)](https://github.com/nats-io/nats.net/actions/workflows/test_windows.yml?query=branch%3Amain)
[![Slack](https://img.shields.io/badge/chat-on%20slack-green)](https://slack.nats.io)

# NATS .NET

NATS .NET is a client library designed to connect to the NATS messaging server,
fully supporting all NATS features.
It integrates seamlessly with modern .NET asynchronous interfaces such as
async enumerables and channels, and leverages advanced .NET memory, buffer and IO features. (supports server v2.11)

### Check out [DOCS](https://nats-io.github.io/nats.net/) for guides and examples.

**Additionally check out [NATS by example](https://natsbyexample.com) - An evolving collection of runnable, cross-client reference examples for NATS.**

> [!NOTE]
> **Don't confuse NuGet packages!**
> NATS .NET package on NuGet is called [NATS.Net](https://www.nuget.org/packages/NATS.Net).
> There is another package called `NATS.Client` which is the older version of the client library
> and will be deprecated eventually.

> [!TIP]
> NATS .NET now supports **.NET Standard** 2.0 and 2.1 along with .NET 6.0 and 8.0,
> which means you can also use it with **.NET Framework** 4.6.2+ and **Unity** 2018.1+.

### What is NATS?

NATS is a high-performance, secure, distributed messaging system.
It's a connective technology tailored for modern distributed systems,
facilitating efficient addressing, discovery, and message exchange.
It supports dynamic service and stream processing across various locations and devices,
enhancing mobility, security, and independence from traditional constraints such as DNS.

Head over to [NATS documentation](https://docs.nats.io/nats-concepts/overview) for more information.

## NATS .NET Goals

- Only support Async I/O (async/await)
- Target .NET Standard 2.0, 2.1, and the two most recent .NET LTS releases (currently .NET 6.0 & .NET 8.0)

## Packages

- **NATS.Net**: Meta package that includes all other packages except extensions
- **NATS.Client.Core**: [Core NATS](https://docs.nats.io/nats-concepts/core-nats)
- **NATS.Client.JetStream**: [JetStream](https://docs.nats.io/nats-concepts/jetstream)
- **NATS.Client.KeyValueStore**: [Key/Value Store](https://docs.nats.io/nats-concepts/jetstream/key-value-store)
- **NATS.Client.ObjectStore**: [Object Store](https://docs.nats.io/nats-concepts/jetstream/obj_store)
- **NATS.Client.Services**: [Services](https://docs.nats.io/using-nats/developer/services)
- **NATS.Client.Simplified**: simplify common use cases especially for beginners
- **NATS.Client.Serializers.Json**: JSON serializer for ad-hoc types
- **NATS.Extensions.Microsoft.DependencyInjection**: extension to configure DI container

## Contributing

You are welcome to contribute to this project. Here are some steps to get you started:

### Reporting Bugs and Feature Requests

You can report bugs and request features
by opening an [issue on GitHub](https://github.com/nats-io/nats.net/issues/new/choose).

### Join the Community

You can join the community asking questions, sharing ideas, and helping others:

- Join the [NATS Slack](https://slack.nats.io) and find us on the `#dotnet` channel
- Join the discussion on [GitHub Discussions](https://github.com/nats-io/nats.net/discussions)
- Follow us on X [@nats_io](https://x.com/nats_io)

### Contributing Code

> [!NOTE]
> Please make sure to **sign your commits**. All commits must be signed before a _Pull Request_ can be merged.

- Read the [Contributor Guide](CONTRIBUTING.md)
- Fork the repository and create a branch
- Open `NATS.Net.sln` solution in Visual Studio, Rider or VS Code (or any other editor of your choice)
- Make changes and write tests
- Run tests against a locally installed NATS server in your PATH
- Note that some tests are still not reliable locally, so CI will run all tests
- For a quick check, run `NATS.Client.Platform.Windows.Tests` which is a subset of tests that should pass on Windows
- You can also locally run `NATS.Client.CoreUnit.Tests` and `NATS.Client.Core2.Tests` which are more stable
- Run `dotnet format` at root directory of project to clear warnings that can be auto-formatted
- Run `dotnet build` at root directory and make sure there are no errors or warnings
- Submit a pull request

Please also check out the [Contributor Guide](CONTRIBUTING.md) and [Code of Conduct](CODE-OF-CONDUCT.md).

## Attribution

This library is based on the excellent work in [Cysharp/AlterNats](https://github.com/Cysharp/AlterNats)
