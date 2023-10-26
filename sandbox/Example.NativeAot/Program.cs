using NATS.Client.Core;

await using var nats = new NatsConnection();

await using var sub = await nats.SubscribeAsync<int>("foo");

await nats.PingAsync();

await nats.PublishAsync("foo", 1);

var msg = await sub.Msgs.ReadAsync();

Console.WriteLine(msg.Data);
