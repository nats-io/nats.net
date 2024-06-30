using System.Threading.Channels;
using NATS.Client.Core.Tests;

namespace NATS.Client.JetStream.Tests;

public class NatsJSContextFactoryTest
{
    private readonly ITestOutputHelper _output;

    public NatsJSContextFactoryTest(ITestOutputHelper output) => _output = output;

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
        var factory = new NatsJSContextFactory();

        // Act
        var context = factory.CreateContext(connection);

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_Context_WithOpts_Test()
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
        var factory = new NatsJSContextFactory();
        var opts = new NatsJSOpts(connection.Opts);

        // Act
        var context = factory.CreateContext(connection, opts);

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public void Create_Context_WithOptsAndMockConnection_Test()
    {
        // Arrange
        var connection = new MockConnection();
        var factory = new NatsJSContextFactory();
        var opts = new NatsJSOpts(connection.Opts);

        // Act
        var context = () => factory.CreateContext(connection, opts);

        // Assert
        context.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Context_WithMockConnection_Test()
    {
        // Arrange
        var connection = new MockConnection();
        var factory = new NatsJSContextFactory();

        // Act
        var context = () => factory.CreateContext(connection);

        // Assert
        context.Should().Throw<ArgumentException>();
    }

    public class MockConnection : INatsConnection
    {
#pragma warning disable CS0067
        public event AsyncEventHandler<NatsEventArgs>? ConnectionDisconnected;

        public event AsyncEventHandler<NatsEventArgs>? ConnectionOpened;

        public event AsyncEventHandler<NatsEventArgs>? ReconnectFailed;

        public event AsyncEventHandler<NatsMessageDroppedEventArgs>? MessageDropped;
#pragma warning restore CS0067

        public INatsServerInfo? ServerInfo { get; } = null;

        public NatsOpts Opts { get; } = new();

        public NatsConnectionState ConnectionState { get; } = NatsConnectionState.Closed;

        public NatsHeaderParser HeaderParser { get; }

        public SubscriptionManager SubscriptionManager { get; }

        public INatsConnection Connection { get; }

        public ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask PublishAsync<T>(string subject, T data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask<INatsSub<T>> SubscribeCoreAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public string NewInbox() => throw new NotImplementedException();

        public ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
            string subject,
            TRequest? data,
            NatsHeaders? headers = default,
            INatsSerialize<TRequest>? requestSerializer = default,
            INatsDeserialize<TReply>? replySerializer = default,
            NatsPubOpts? requestOpts = default,
            NatsSubOpts? replyOpts = default,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ValueTask<NatsMsg<TReply>> RequestAsync<TReply>(string subject, INatsDeserialize<TReply>? replySerializer = default, NatsSubOpts? replyOpts = default, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<NatsMsg<TReply>> RequestManyAsync<TRequest, TReply>(
            string subject,
            TRequest? data,
            NatsHeaders? headers = default,
            INatsSerialize<TRequest>? requestSerializer = default,
            INatsDeserialize<TReply>? replySerializer = default,
            NatsPubOpts? requestOpts = default,
            NatsSubOpts? replyOpts = default,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ValueTask ConnectAsync() => throw new NotImplementedException();

        public ValueTask<NatsSub<TReply>> RequestSubAsync<TRequest, TReply>(string subject, TRequest? data, NatsHeaders? headers = default, INatsSerialize<TRequest>? requestSerializer = default, INatsDeserialize<TReply>? replySerializer = default, NatsPubOpts? requestOpts = default, NatsSubOpts? replyOpts = default, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public void OnMessageDropped<T>(NatsSubBase natsSub, int pending, NatsMsg<T> msg) => throw new NotImplementedException();

        public BoundedChannelOptions GetChannelOpts(NatsOpts connectionOpts, NatsSubChannelOpts? subChannelOpts) => throw new NotImplementedException();

        public ValueTask DisposeAsync() => throw new NotImplementedException();
    }
}
