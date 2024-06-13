using NATS.Client.Core;
using Xunit.Abstractions;

namespace NATS.Client.Platform.Windows.Tests;

public class UnitTest1(ITestOutputHelper output)
{
    [Fact]
    public async Task Test1()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });
        var rtt = await nats.PingAsync();
        output.WriteLine($"rtt={rtt}");
    }
}
