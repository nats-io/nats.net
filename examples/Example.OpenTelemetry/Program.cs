/****************************************************************************

                         OpenTelemetry Example

Both apps export traces and metrics via OTLP. Point them at any backend that
accepts OTLP (Aspire dashboard, Jaeger for traces, Grafana stack, etc.).

(1) Start Aspire dashboard:

```powershell
> docker run --rm -it `
  -p 18888:18888 -p 4317:18889 `
  -e DASHBOARD__OTLP__AUTHMODE=Unsecured `
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

or

```bash
$ docker run --rm -it \
  -p 18888:18888 -p 4317:18889 \
  -e DASHBOARD__OTLP__AUTHMODE=Unsecured \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

(2) In different terminals run:

```
nats-server
```

```
dotnet run -- service
```

```
dotnet run -- client
```

****************************************************************************/

using Example.OpenTelemetry;

if (args.Length > 0 && args[0] == "client")
{
    await ClientApp.Run();
}
else if (args.Length > 0 && args[0] == "service")
{
    await ServiceApp.Run();
}
else
{
    Console.Error.WriteLine("Usage: dotnet run -- <client|service>");
}
