/****************************************************************************

                         OpenTelemetry Example

(1) Run Jaeger locally and then run the client and server apps.

https://www.jaegertracing.io/download/

https://medium.com/jaegertracing/introducing-native-support-for-opentelemetry-in-jaeger-eb661be8183c

```powershell
> $env:COLLECTOR_OTLP_ENABLED=true
> jaeger-all-in-one.exe
```

or

```bash
$ COLLECTOR_OTLP_ENABLED=true jaeger-all-in-one
```

(2) Jaeger UI default URL http://localhost:16686/search

(3) In different terminals run:

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
