namespace NATS.Client.Core.Internal
{
    internal sealed class TcpFactory : INatsSocketConnectionFactory
    {
        public static INatsSocketConnectionFactory Default { get; } = new TcpFactory();

        public async ValueTask<INatsSocketConnection> ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken)
        {
            var conn = new TcpConnection(opts);
            await conn.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            return conn;
        }
    }
}
