using NATS.Client.Core.Tests;
using NATS.Client.JetStream.Models;

namespace NATS.Client.KeyValueStore.Tests;

public class NatsKVContextFactoryTest
{
    private readonly ITestOutputHelper _output;

    public NatsKVContextFactoryTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_Context_Test()
    {
        // Arrange
        await using var server = NatsServer.Start(
            outputHelper: _output,
            opts: new NatsServerOptsBuilder()
                .UseTransport(TransportType.Tcp)
                .Trace()
                .UseJetStream()
                .Build());
        await using var connection = server.CreateClientConnection(new NatsOpts { RequestTimeout = TimeSpan.FromSeconds(10) });
        var jsFactory = new NatsJSContextFactory();
        var jsContext = jsFactory.CreateContext(connection);
        var factory = new NatsKVContextFactory();

        // Act
        var context = factory.CreateContext(jsContext);

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public void Create_Context_WithMockConnection_Test()
    {
        // Arrange
        var mockJsContext = new MockJsContext();
        var factory = new NatsKVContextFactory();

        // Act
        var context = () => factory.CreateContext(mockJsContext);

        // Assert
        context.Should().Throw<ArgumentException>();
    }

    public class MockJsContext : INatsJSContext
    {
        public ValueTask<INatsJSConsumer> CreateOrderedConsumerAsync(string stream, NatsJSOrderedConsumerOpts? opts = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<INatsJSConsumer> CreateOrUpdateConsumerAsync(string stream, ConsumerConfig config, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<INatsJSConsumer> CreateConsumerAsync(string stream, ConsumerConfig config, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<INatsJSConsumer> UpdateConsumerAsync(string stream, ConsumerConfig config, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<INatsJSConsumer> GetConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<INatsJSConsumer> ListConsumersAsync(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<string> ListConsumerNamesAsync(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<bool> DeleteConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<ConsumerPauseResponse> PauseConsumerAsync(string stream, string consumer, DateTimeOffset pauseUntil, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<bool> ResumeConsumerAsync(string stream, string consumer, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<AccountInfoResponse> GetAccountInfoAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<PubAckResponse> PublishAsync<T>(string subject, T? data, INatsSerialize<T>? serializer = default, NatsJSPubOpts? opts = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<INatsJSStream> CreateStreamAsync(StreamConfig config, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<bool> DeleteStreamAsync(string stream, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<StreamPurgeResponse> PurgeStreamAsync(string stream, StreamPurgeRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<StreamMsgDeleteResponse> DeleteMessageAsync(string stream, StreamMsgDeleteRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<INatsJSStream> GetStreamAsync(string stream, StreamInfoRequest? request = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<NatsJSStream> UpdateStreamAsync(StreamConfig request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<INatsJSStream> ListStreamsAsync(string? subject = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<string> ListStreamNamesAsync(string? subject = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<NatsJSPublishConcurrentFuture> PublishConcurrentAsync<T>(string subject, T? data, INatsSerialize<T>? serializer = default, NatsJSPubOpts? opts = default, NatsHeaders? headers = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
