namespace NATS.Client.Core.Internal
{
    internal sealed class WebSocketFactory : INatsSocketConnectionFactory
    {
        public static INatsSocketConnectionFactory Default { get; } = new WebSocketFactory();

        public async ValueTask<INatsSocketConnection> ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken)
        {
            var conn = new WebSocketConnection(opts);
            await conn.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            return conn;
        }
    }
}
