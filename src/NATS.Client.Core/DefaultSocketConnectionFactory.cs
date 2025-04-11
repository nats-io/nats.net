using Microsoft.Extensions.Logging;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public class DefaultSocketConnectionFactory : ISocketConnectionFactory
{
    private static readonly Lazy<DefaultSocketConnectionFactory> LazyInstance = new(() => new DefaultSocketConnectionFactory());

    public static DefaultSocketConnectionFactory Instance => LazyInstance.Value;

    public async Task<ISocketConnection> OnConnectionAsync(NatsUri uri, NatsConnection natsConnection, ILogger<NatsConnection> logger)
    {
        var target = (uri.Host, uri.Port);
        target = await BeforeConnectingAsync(natsConnection, logger, target).ConfigureAwait(false);

        logger.LogInformation(NatsLogEvents.Connection, "Try to connect NATS {0}", uri);
        ISocketConnection? socket;

        var opts = natsConnection.Opts;
        if (uri.IsWebSocket)
        {
            var webSocketConnection = new WebSocketConnection();
            await webSocketConnection.ConnectAsync(uri, opts).ConfigureAwait(false);
            socket = webSocketConnection;
        }
        else
        {
            var tcpConnection = new TcpConnection(logger);
            await tcpConnection.ConnectAsync(target.Host, target.Port, opts.ConnectTimeout).ConfigureAwait(false);
            socket = tcpConnection;
        }

        return socket;
    }

    public async Task<ISocketConnection> OnReconnectionAsync(NatsUri uri, NatsConnection natsConnection, ILogger<NatsConnection> logger, int reconnectCount)
    {
        if (natsConnection.OnConnectingAsync != null)
        {
            var target = (uri.Host, uri.Port);
            logger.LogInformation(
                NatsLogEvents.Connection,
                "Try to invoke OnConnectingAsync before connect to NATS [{ReconnectCount}]",
                reconnectCount);
            var newTarget = await natsConnection.OnConnectingAsync(target).ConfigureAwait(false);

            if (newTarget.Host != target.Host || newTarget.Port != target.Port)
            {
                uri = uri.CloneWith(newTarget.Host, newTarget.Port);
            }
        }

        logger.LogInformation(NatsLogEvents.Connection, "Tried to connect NATS {Url} [{ReconnectCount}]", uri, reconnectCount);
        ISocketConnection? socket;
        var opts = natsConnection.Opts;
        if (uri.IsWebSocket)
        {
            logger.LogDebug(NatsLogEvents.Connection, "Trying to reconnect using WebSocket {Url} [{ReconnectCount}]", uri, reconnectCount);

            var webSocketConnection = new WebSocketConnection();
            await webSocketConnection.ConnectAsync(uri, opts).ConfigureAwait(false);
            socket = webSocketConnection;
        }
        else
        {
            logger.LogDebug(NatsLogEvents.Connection, "Trying to reconnect using TCP {Url} [{ReconnectCount}]", uri, reconnectCount);

            var tcpConnection = new TcpConnection(logger);
            await tcpConnection.ConnectAsync(uri.Host, uri.Port, opts.ConnectTimeout).ConfigureAwait(false);
            socket = await AfterReconnectingAsync(natsConnection, uri, opts, logger, reconnectCount, tcpConnection).ConfigureAwait(false);
        }

        return socket;
    }

    public async Task<(string host, int port)> BeforeConnectingAsync(
        NatsConnection natsConnection,
        ILogger<NatsConnection> logger,
        (string host, int port) target)
    {
        if (natsConnection.OnConnectingAsync == null) return target;

        logger.LogInformation(NatsLogEvents.Connection, "Try to invoke OnConnectingAsync before connect to NATS");
        target = await natsConnection.OnConnectingAsync(target).ConfigureAwait(false);

        return target;
    }

    public async Task<ISocketConnection> AfterReconnectingAsync(
        NatsConnection natsConnection,
        NatsUri uri,
        NatsOpts opts,
        ILogger<NatsConnection> logger,
        int reconnectCount,
        TcpConnection tcpConnection)
    {
        if (opts.TlsOpts.EffectiveMode(uri) != TlsMode.Implicit) return tcpConnection;

        // upgrade TcpConnection to SslConnection
        logger.LogDebug(
            NatsLogEvents.Connection,
            "Trying to reconnect and upgrading to TLS {Url} [{ReconnectCount}]",
            uri,
            reconnectCount);
        var sslConnection = tcpConnection.UpgradeToSslStreamConnection(opts.TlsOpts);
        await sslConnection.AuthenticateAsClientAsync(natsConnection.FixTlsHost(uri), opts.ConnectTimeout).ConfigureAwait(false);
        return sslConnection;
    }
}
