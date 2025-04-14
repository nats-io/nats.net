namespace NATS.Client.Core.Internal
{
    internal sealed class TcpFactory : INatsSocketConnectionFactory
    {
        public static INatsSocketConnectionFactory Default { get; } = new TcpFactory();

        public async ValueTask<INatsSocketConnection> ConnectAsync(Uri uri, NatsOpts opts, CancellationToken cancellationToken)
        {
            var conn = new TcpConnection();
            await conn.ConnectAsync(uri, opts, cancellationToken).ConfigureAwait(false);

            return conn;
        }
    }
}
