# Updating Documentation

As well as being able to edit pages on GitHub, you can also edit and update this documentation,
view locally and submit a Pull Request to be included in this documentation site.

## Running DocFX locally

Clone the NATS.Net ([nats-io/nats.net.v2](https://github.com/nats-io/nats.net.v2)) repository, then run `docfx` local server to view this documentation site.
You mush have [DocFX installed](https://dotnet.github.io/docfx/):

```
dotnet tool update -g docfx
```

Generate API documentation and run local server:
```
$ git clone https://github.com/nats-io/nats.net.v2.git
$ cd nats.net.v2/docs
$ docfx docfx.json --serve
```
