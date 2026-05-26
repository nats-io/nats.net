/****************************************************************************

                         OpenTelemetry Example

Both apps export traces and metrics via OTLP. Point them at any backend that
accepts OTLP (Jaeger for traces, an OTel collector, Grafana stack, etc.).

(1) Quickest local setup: Jaeger all-in-one accepts OTLP for traces.

https://www.jaegertracing.io/download/

```powershell
> $env:COLLECTOR_OTLP_ENABLED=true
> jaeger-all-in-one.exe
```

or

```bash
$ COLLECTOR_OTLP_ENABLED=true jaeger-all-in-one
```

Jaeger UI: http://localhost:16686/search (traces only).

For metrics, route the OTLP stream through an OpenTelemetry Collector to
Prometheus/Grafana or any OTLP-capable backend.

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
