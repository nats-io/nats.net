using System.Net;
using System.Text;
using System.Threading.Channels;
using NATS.Client.Core.Tests;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.JetStream.Tests;

public class NatsJSContextFactoryTest
{
    private readonly ITestOutputHelper _output;

    public NatsJSContextFactoryTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Create_Context_Test()
    {
        // Arrange
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestTimeout = TimeSpan.FromSeconds(10) });
        var factory = new NatsJSContextFactory();

        // Act
        var context = factory.CreateContext(nats);

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_Context_WithOpts_Test()
    {
        // Arrange
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url, RequestTimeout = TimeSpan.FromSeconds(10) });
        var factory = new NatsJSContextFactory();
        var opts = new NatsJSOpts(nats.Opts);

        // Act
        var context = factory.CreateContext(nats, opts);

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

        public event AsyncEventHandler<NatsSlowConsumerEventArgs>? SlowConsumerDetected;

        public event AsyncEventHandler<NatsLameDuckModeActivatedEventArgs>? LameDuckModeActivated;
#pragma warning restore CS0067

        public INatsServerInfo? ServerInfo { get; } = null;

        public NatsOpts Opts { get; } = new();

        public INatsConnection Connection => this;

        public NatsConnectionState ConnectionState { get; } = NatsConnectionState.Closed;

        public INatsSubscriptionManager SubscriptionManager { get; } = new TestSubscriptionManager();

        public NatsHeaderParser HeaderParser { get; } = new NatsHeaderParser(Encoding.UTF8);

        public Func<(string Host, int Port), ValueTask<(string Host, int Port)>>? OnConnectingAsync { get; set; }

        public Func<INatsSocketConnection, ValueTask<INatsSocketConnection>>? OnSocketAvailableAsync { get; set; }

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

        public void OnMessageDropped<T>(NatsSubBase natsSub, int pending, NatsMsg<T> msg) => throw new NotImplementedException();

        public ValueTask AddSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public BoundedChannelOptions GetBoundedChannelOpts(NatsSubChannelOpts? subChannelOpts) => throw new NotImplementedException();

        public ValueTask<NatsSub<TReply>> CreateRequestSubAsync<TRequest, TReply>(string subject, TRequest? data, NatsHeaders? headers = default, INatsSerialize<TRequest>? requestSerializer = default, INatsDeserialize<TReply>? replySerializer = default, NatsPubOpts? requestOpts = default, NatsSubOpts? replyOpts = default, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask ConnectAsync() => throw new NotImplementedException();

        public ValueTask ReconnectAsync() => throw new NotImplementedException();

        public ValueTask DisposeAsync() => throw new NotImplementedException();
    }
}

public class TestSubscriptionManager : INatsSubscriptionManager
{
    public ValueTask RemoveAsync(NatsSubBase sub) => throw new NotImplementedException();
}
