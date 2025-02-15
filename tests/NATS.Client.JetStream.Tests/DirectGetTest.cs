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
            var streamMsgBatchGetRequest = new StreamMsgBatchGetRequest { NextBySubject = "stream_disable.x" };
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

        await js.PublishAsync("stream_enable.x", new TestData { Test = 1 }, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken);

        var streamMsgBatchGetRequest = new StreamMsgBatchGetRequest { NextBySubject = "stream_enable.x", Batch = 3 };
        await foreach (var msg in stream.GetBatchDirectAsync(streamMsgBatchGetRequest, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken))
        {
            testDataList.Add(msg.Data);
        }

        Assert.Single(testDataList);
    }

    [SkipIfNatsServer(versionEarlierThan: "2.11")]
    public async Task Direct_get_by_multi_last()
    {
        var testDataList = new List<TestData?>();
        await using var server = NatsServer.StartJS();
        var nats = server.CreateClientConnection();
        var js = new NatsJSContext(nats);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var cancellationToken = cts.Token;
        var streamConfig = new StreamConfig("multiLast", new[] { "multiLast.*" }) { AllowDirect = true };

        var stream = await js.CreateStreamAsync(streamConfig, cancellationToken);

        await js.PublishAsync("multiLast.x", new TestData { Test = 1 }, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken);
        await js.PublishAsync("multiLast.y", new TestData { Test = 2 }, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken);

        await foreach (var msg in stream.GetBatchDirectAsync(["multiLast.x", "multiLast.y"], 4, TestDataJsonSerializer<TestData>.Default, cancellationToken: cancellationToken))
        {
            testDataList.Add(msg.Data);
        }

        Assert.Equal(2, testDataList.Count);
    }
}
