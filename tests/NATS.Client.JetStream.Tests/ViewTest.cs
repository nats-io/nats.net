using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class ViewTest
{
    private readonly ITestOutputHelper _output;

    public ViewTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Get_all_msgs()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // await using var server = NatsServer.Start(
        //     outputHelper: _output,
        //     opts: new NatsServerOptsBuilder()
        //         .UseTransport(TransportType.Tcp)
        //         .Trace()
        //         .UseJetStream()
        //         .Build());

        // var (nats, proxy) = server.CreateProxiedClientConnection();
        var nats = new NatsConnection(NatsOpts.Default);
        var js = new NatsJSContext(nats);

        await nats.ConnectAsync();

        // await js.CreateStreamAsync("s1", new[] {"s1.*"}, cts.Token);

        // for (var i = 0; i < 30; i++)
        // {
        //     var ack = await js.PublishAsync("s1.foo", new TestData {Test = i}, cancellationToken: cts.Token);
        //     ack.EnsureSuccess();
        // }

        var view = await js.GetViewAsync("s1", "s1.*", cts.Token);

        var fetchAllMsgs = await view.FetchAllAsync<TestData>(cancellationToken: cts.Token);

        var count = 0;
        await foreach (var natsJSMsg in fetchAllMsgs.Msgs.ReadAllAsync(cts.Token))
        {
            await natsJSMsg.AckAsync(cancellationToken: cts.Token);

            count++;
        }

        Assert.Equal(30, count);
    }

    private record TestData
    {
        public int Test { get; init; }
    }
}
