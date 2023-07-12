using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;

namespace NATS.Client.Core.Tests;

public class JetStreamTest
{
    private readonly ITestOutputHelper _output;

    public JetStreamTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_stream_test()
    {
        await using var server = new NatsServer(new NullOutputHelper(), TransportType.Tcp, new NatsServerOptionsBuilder().UseTransport(TransportType.Tcp).UseJetStream().Build());
        await using var nats = server.CreateClientConnection();

        // Create stream
        {
            var context = new JSContext(nats, new JSOptions());
            var stream = await context.CreateStream(request: stream =>
            {
                stream.Name = "events";
                stream.Subjects = new[] { "events" };
            });
            _output.WriteLine($"RCV: {stream.Response}");
        }

        // Handle exceptions
        {
            var context = new JSContext(nats, new JSOptions());
            try
            {
                var stream = await context.CreateStream(request: stream =>
                {
                    stream.Name = "events";
                    stream.Subjects = new[] { "events" };
                });
                _output.WriteLine($"RCV: {stream.Response}");
            }
            catch (NatsSubException e)
            {
                var payload = e.Payload.Dump();
                var headers = e.Headers.Dump();
                _output.WriteLine($"{e}");
                _output.WriteLine($"headers: {headers}");
                _output.WriteLine($"payload: {payload}");
            }
        }
    }
}
