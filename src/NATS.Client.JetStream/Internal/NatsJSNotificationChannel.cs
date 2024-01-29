using System.Threading.Channels;

namespace NATS.Client.JetStream.Internal;

internal class NatsJSNotificationChannel : IAsyncDisposable
{
    private readonly Func<INatsJSNotification, CancellationToken, Task> _notificationHandler;
    private readonly Action<Exception> _exceptionHandler;
    private readonly CancellationToken _cancellationToken;
    private readonly Channel<INatsJSNotification> _channel;
    private readonly Task _loop;

    public NatsJSNotificationChannel(
        Func<INatsJSNotification, CancellationToken, Task> notificationHandler,
        Action<Exception> exceptionHandler,
        CancellationToken cancellationToken)
    {
        _notificationHandler = notificationHandler;
        _exceptionHandler = exceptionHandler;
        _cancellationToken = cancellationToken;
        _channel = Channel.CreateBounded<INatsJSNotification>(new BoundedChannelOptions(128)
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _loop = Task.Run(NotificationLoop, CancellationToken.None);
    }

    public void Notify(INatsJSNotification notification) => _channel.Writer.TryWrite(notification);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task NotificationLoop()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_cancellationToken))
            {
                while (_channel.Reader.TryRead(out var notification))
                {
                    try
                    {
                        await _notificationHandler(notification, _cancellationToken);
                    }
                    catch (Exception e)
                    {
                        _exceptionHandler(e);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
