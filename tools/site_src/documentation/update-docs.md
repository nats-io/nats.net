# Updating Documentation

As well as being able to edit pages on GitHub, you can also edit and update this documentation,
view locally and submit a Pull Request to be included in this documentation site.

## Running DocFX locally

Clone the NATS .NET ([nats-io/nats.net](https://github.com/nats-io/nats.net)) repository, then run `docfx` local server to view this documentation site.
You mush have [DocFX installed](https://dotnet.github.io/docfx/):

```
dotnet tool update -g docfx
```

Generate API documentation and run local server:
```
git clone https://github.com/nats-io/nats.net.git
cd nats.net/tools/site_src
docfx docfx.json --serve
```

You might not be a .NET developer, but still want to contribute to the documentation.
You can install the .NET SDK and use the `docfx` tool to generate the documentation.
Download the .NET SDK from [dot.net](https://dot.net).
