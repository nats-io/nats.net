using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace NATS.Client.JetStream.Internal;

internal class NatsJSNotificationChannel : IAsyncDisposable
{
    private readonly Func<INatsJSNotification, Task> _notificationHandler;
    private readonly Action<Exception> _exceptionHandler;
    private readonly Channel<INatsJSNotification> _channel;
    private readonly Task _loop;

    public NatsJSNotificationChannel(
        Func<INatsJSNotification, Task> notificationHandler,
        Action<Exception> exceptionHandler)
    {
        _notificationHandler = notificationHandler;
        _exceptionHandler = exceptionHandler;
        _channel = Channel.CreateBounded<INatsJSNotification>(new BoundedChannelOptions(128)
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _loop = Task.Run(NotificationLoop);
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
            while (await _channel.Reader.WaitToReadAsync())
            {
                while (_channel.Reader.TryRead(out var notification))
                {
                    try
                    {
                        await _notificationHandler(notification);
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
