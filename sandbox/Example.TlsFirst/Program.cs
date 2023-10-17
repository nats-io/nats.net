using NATS.Client.Core;

// await using var nats = new NatsConnection();
await using var nats = new NatsConnection(NatsOpts.Default with { TlsOpts = new NatsTlsOpts { Mode = TlsMode.Implicit, InsecureSkipVerify = true, } });
await nats.ConnectAsync();
var timeSpan = await nats.PingAsync();
Console.WriteLine($"{timeSpan}");
