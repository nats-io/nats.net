using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Tests;

public class DirectGetTest
{
    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Direct_get_when_stream_disable()
    {
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var streamConfig = new StreamConfig("stream_disable", new[] { "stream_disable.x" });

        var stream = await js.CreateStreamAsync(streamConfig, cancellationToken);

        async Task GetBatchAction()
        {
            var streamMsgBatchGetRequest = new StreamMsgBatchGetRequest { Subject = "stream_disable.x" };
            await foreach (var unused in stream.GetBatchDirectAsync<string>(streamMsgBatchGetRequest, cancellationToken: cancellationToken))
            {
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(GetBatchAction);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Direct_get_when_stream_enable()
    {
        var testDataList = new List<TestData?>();
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var cancellationToken = cts.Token;
        var streamConfig = new StreamConfig("stream_enable", new[] { "stream_enable.x" }) { AllowDirect = true };

        var stream = await js.CreateStreamAsync(streamConfig, cancellationToken);

        for (var i = 0; i < 1; i++)
        {
            await js.PublishAsync("stream_enable.x", new TestData { Test = i }, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken);
        }

        var streamMsgBatchGetRequest = new StreamMsgBatchGetRequest { Subject = "stream_enable.x", Batch = 3 };
        await foreach (var msg in stream.GetBatchDirectAsync(streamMsgBatchGetRequest, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken))
        {
            testDataList.Add(msg.Data);
        }

        Assert.Single(testDataList);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Direct_get_with_eobCode()
    {
        var testDataList = new List<TestData?>();
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var cancellationToken = cts.Token;
        var streamConfig = new StreamConfig("eobCode", new[] { "eobCode.x" }) { AllowDirect = true };

        var stream = await js.CreateStreamAsync(streamConfig, cancellationToken);

        for (var i = 0; i < 1; i++)
        {
            await js.PublishAsync("eobCode.x", new TestData { Test = i }, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken);
        }

        var streamMsgBatchGetRequest = new StreamMsgBatchGetRequest { Subject = "eobCode.x", Batch = 3 };
        await foreach (var msg in stream.GetBatchDirectAsync(streamMsgBatchGetRequest, TestDataJsonSerializer<TestData>.Default, includeEob: true, cancellationToken: cancellationToken))
        {
            testDataList.Add(msg.Data);
        }

        Assert.Equal(2, testDataList.Count);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Direct_get_min_sequence()
    {
        var testDataList = new List<TestData?>();
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var cancellationToken = cts.Token;
        var streamConfig = new StreamConfig("min_sequence", new[] { "min_sequence.x" }) { AllowDirect = true };

        var stream = await js.CreateStreamAsync(streamConfig, cancellationToken);

        for (var i = 0; i < 3; i++)
        {
            await js.PublishAsync("min_sequence.x", new TestData { Test = i }, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken);
        }

        var streamMsgBatchGetRequest = new StreamMsgBatchGetRequest { Subject = "min_sequence.x", Batch = 1, MinSequence = 3 };
        await foreach (var msg in stream.GetBatchDirectAsync(streamMsgBatchGetRequest, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken))
        {
            testDataList.Add(msg.Data);
        }

        Assert.Single(testDataList);
        Assert.Equal(2, testDataList[0]?.Test);
    }
}
