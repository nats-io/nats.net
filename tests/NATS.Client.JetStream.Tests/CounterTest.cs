using NATS.Client.Core2.Tests;
using NATS.Client.JetStream.Models;
using NATS.Client.TestUtilities;

namespace NATS.Client.JetStream.Tests;

[Collection("nats-server")]
public class CounterTest
{
    private readonly ITestOutputHelper _output;
    private readonly NatsServerFixture _server;

    public CounterTest(ITestOutputHelper output, NatsServerFixture server)
    {
        _output = output;
        _server = server;
    }

    [SkipIfNatsServer(versionEarlierThan: "2.12")]
    public async Task Counter_functionality_test()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        // Create a stream with counter support enabled
        var streamConfig = new StreamConfig
        {
            Name = $"{prefix}counter-stream",
            Subjects = new[] { $"{prefix}counter.>" },
            AllowMsgCounter = true,
        };

        await js.CreateStreamAsync(streamConfig);

        // Publish a message with the counter header
        var headers = new NatsHeaders
        {
            { "Nats-Incr", "+5" },
        };

        var ack = await js.PublishAsync($"{prefix}counter.test", data: Array.Empty<byte>(), headers: headers);

        Assert.Null(ack.Error);
        Assert.NotNull(ack.Value);
        Assert.Equal("5", ack.Value);
        _output.WriteLine($"First publish - Value: {ack.Value}");

        // Publish another message to increment the counter
        headers = new NatsHeaders
        {
            { "Nats-Incr", "+3" },
        };

        ack = await js.PublishAsync($"{prefix}counter.test", data: Array.Empty<byte>(), headers: headers);

        Assert.Null(ack.Error);
        Assert.NotNull(ack.Value);
        Assert.Equal("8", ack.Value);
        _output.WriteLine($"Second publish - Value: {ack.Value}");

        // Test subtract operation
        headers = new NatsHeaders
        {
            { "Nats-Incr", "-2" },
        };

        ack = await js.PublishAsync($"{prefix}counter.test", data: Array.Empty<byte>(), headers: headers);

        Assert.Null(ack.Error);
        Assert.NotNull(ack.Value);
        Assert.Equal("6", ack.Value);
        _output.WriteLine($"Third publish (subtract) - Value: {ack.Value}");

        // Test a different counter (different subject)
        headers = new NatsHeaders
        {
            { "Nats-Incr", "+10" },
        };

        ack = await js.PublishAsync($"{prefix}counter.test2", data: Array.Empty<byte>(), headers: headers);

        Assert.Null(ack.Error);
        Assert.NotNull(ack.Value);
        Assert.Equal("10", ack.Value);
        _output.WriteLine($"Different counter - Value: {ack.Value}");

        // Verify the stream message count
        var stream = await js.GetStreamAsync($"{prefix}counter-stream");
        Assert.Equal(4u, stream.Info.State.Messages);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.12")]
    public async Task Counter_without_AllowMsgCounter_should_return_error()
    {
        await using var nats = new NatsConnection(new NatsOpts { Url = _server.Url });
        var prefix = _server.GetNextId();
        var js = new NatsJSContext(nats);

        // Create a stream without counter support
        var streamConfig = new StreamConfig
        {
            Name = $"{prefix}no-counter-stream",
            Subjects = new[] { $"{prefix}nocounter.>" },
            AllowMsgCounter = false, // Explicitly disable counter
        };

        await js.CreateStreamAsync(streamConfig);

        // Publish a message with counter headers (should return an error)
        var headers = new NatsHeaders
        {
            { "Nats-Incr", "+5" },
        };

        var ack = await js.PublishAsync($"{prefix}nocounter.test", data: Array.Empty<byte>(), headers: headers);

        // When counter is disabled, the server returns an error
        Assert.NotNull(ack.Error);
        Assert.Equal(10168, ack.Error.ErrCode); // Error code for "message counters is disabled"
        Assert.Contains("message counters are disabled", ack.Error.Description);
        Assert.Null(ack.Value); // Value should be null when counter is not enabled
        _output.WriteLine($"Error as expected: {ack.Error.Description}");
    }
}
