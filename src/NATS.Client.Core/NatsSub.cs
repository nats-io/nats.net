using System.Buffers;
using System.Threading.Channels;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

internal enum NatsSubEndReason
{
    None,
    MaxMsgs,
    Timeout,
    IdleTimeout,
    StartUpTimeout,
    Exception,
}

public abstract class NatsSubBase : INatsSub
{
    private readonly ISubscriptionManager _manager;
    private readonly Timer? _timeoutTimer;
    private readonly Timer? _idleTimeoutTimer;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _startUpTimeout;
    private readonly TimeSpan _timeout;
    private readonly bool _countPendingMsgs;
    private volatile Timer? _startUpTimeoutTimer;
    private bool _disposed;
    private bool _unsubscribed;
    private bool _endSubscription;
    private int _endReasonRaw;
    private int _pendingMsgs;
    private Exception? _exception = null;

    internal NatsSubBase(
        NatsConnection connection,
        ISubscriptionManager manager,
        string subject,
        NatsSubOpts? opts)
    {
        _manager = manager;
        _pendingMsgs = opts is { MaxMsgs: > 0 } ? opts.Value.MaxMsgs ?? -1 : -1;
        _countPendingMsgs = _pendingMsgs > 0;
        _idleTimeout = opts?.IdleTimeout ?? default;
        _startUpTimeout = opts?.StartUpTimeout ?? default;
        _timeout = opts?.Timeout ?? default;

        Connection = connection;
        Subject = subject;
        QueueGroup = opts?.QueueGroup;

        // Only allocate timers if necessary to reduce GC pressure
        if (_idleTimeout != default)
        {
            // Instead of Timers what we could've used here is a cancellation token source based loop
            // i.e. CancellationTokenSource.CancelAfter(TimeSpan) within a Task.Run(async delegate)
            // They both seem to use internal TimerQueue. The difference is that Timer seem to
            // lead to a relatively simpler implementation but the downside is callback is not
            // async and unsubscribe call has to be fire-and-forget. On the other hand running
            // CancellationTokenSource.CancelAfter in Task.Run(async delegate) gives us the
            // chance to await the unsubscribe call but leaves us to deal with the created task.
            // Since awaiting unsubscribe isn't crucial Timer approach is currently acceptable.
            // If we need an async loop in the future cancellation token source approach can be used.
            _idleTimeoutTimer = new Timer(_ => EndSubscription(NatsSubEndReason.IdleTimeout));
        }

        if (_startUpTimeout != default)
        {
            _startUpTimeoutTimer = new Timer(_ => EndSubscription(NatsSubEndReason.StartUpTimeout));
        }

        if (_timeout != default)
        {
            _timeoutTimer = new Timer(_ => EndSubscription(NatsSubEndReason.Timeout));
        }
    }

    /// <summary>
    /// The subject name to subscribe to.
    /// </summary>
    public string Subject { get; }

    /// <summary>
    /// If specified, the subscriber will join this queue group. Subscribers with the same queue group name,
    /// become a queue group, and only one randomly chosen subscriber of the queue group will
    /// consume a message each time a message is received by the queue group.
    /// </summary>
    public string? QueueGroup { get; }

    public Exception? Exception => Volatile.Read(ref _exception);

    // Hide from public API using explicit interface implementations
    // since INatsSub is marked as internal.
    int? INatsSub.PendingMsgs => _pendingMsgs == -1 ? null : Volatile.Read(ref _pendingMsgs);

    internal NatsSubEndReason EndReason => (NatsSubEndReason)Volatile.Read(ref _endReasonRaw);

    protected NatsConnection Connection { get; }

    void INatsSub.Ready()
    {
        // Let idle timer start with the first message, in case
        // we're allowed to wait longer for the first message.
        if (_startUpTimeoutTimer == null)
            _idleTimeoutTimer?.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

        _startUpTimeoutTimer?.Change(_startUpTimeout, Timeout.InfiniteTimeSpan);
        _timeoutTimer?.Change(dueTime: _timeout, period: Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Complete the message channel, stop timers if they were used and send an unsubscribe
    /// message to the server.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous server UNSUB operation.</returns>
    public ValueTask UnsubscribeAsync()
    {
        lock (this)
        {
            if (_unsubscribed)
                return ValueTask.CompletedTask;
            _unsubscribed = true;
        }

        _timeoutTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _idleTimeoutTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _startUpTimeoutTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        TryComplete();

        return _manager.RemoveAsync(this);
    }

    public ValueTask DisposeAsync()
    {
        lock (this)
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            _disposed = true;
        }

        GC.SuppressFinalize(this);

        var unsubscribeAsync = UnsubscribeAsync();

        _timeoutTimer?.Dispose();
        _idleTimeoutTimer?.Dispose();
        _startUpTimeoutTimer?.Dispose();

        if (Exception != null)
            throw Exception;

        return unsubscribeAsync;
    }

    ValueTask INatsSub.ReceiveAsync(
        string subject,
        string? replyTo,
        ReadOnlySequence<byte>? headersBuffer,
        ReadOnlySequence<byte> payloadBuffer) =>
        ReceiveInternalAsync(subject, replyTo, headersBuffer, payloadBuffer);

    internal void ClearException() => Interlocked.Exchange(ref _exception, null);

    protected abstract ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer);

    protected void SetException(Exception exception)
    {
        Interlocked.Exchange(ref _exception, exception);
        EndSubscription(NatsSubEndReason.Exception);
    }

    protected void ResetIdleTimeout()
    {
        _idleTimeoutTimer?.Change(dueTime: _idleTimeout, period: Timeout.InfiniteTimeSpan);

        // Once the first message is received we don't need to keep resetting the start-up timer
        if (_startUpTimeoutTimer != null)
        {
            _startUpTimeoutTimer.Change(dueTime: Timeout.InfiniteTimeSpan, period: Timeout.InfiniteTimeSpan);
            _startUpTimeoutTimer = null;
        }
    }

    protected void DecrementMaxMsgs()
    {
        if (!_countPendingMsgs)
            return;
        var maxMsgs = Interlocked.Decrement(ref _pendingMsgs);
        if (maxMsgs == 0)
            EndSubscription(NatsSubEndReason.MaxMsgs);
    }

    protected abstract void TryComplete();

    private void EndSubscription(NatsSubEndReason reason)
    {
        lock (this)
        {
            if (_endSubscription)
                return;
            _endSubscription = true;
        }

        Interlocked.Exchange(ref _endReasonRaw, (int)reason);

        // Stops timers and completes channel writer to exit any message iterators
        // synchronously, which is fine, however, we're not able to wait for
        // UNSUB message to be sent to the server. If any message arrives after this point
        // channel writer will ignore the message and we would effectively drop it.
        var fireAndForget = UnsubscribeAsync();
    }
}

public sealed class NatsSub : NatsSubBase
{
    private static readonly BoundedChannelOptions DefaultChannelOptions =
        new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        };

    private readonly Channel<NatsMsg> _msgs;

    internal NatsSub(NatsConnection connection, ISubscriptionManager manager, string subject, NatsSubOpts? opts)
        : base(connection, manager, subject, opts) =>
        _msgs = Channel.CreateBounded<NatsMsg>(
            GetChannelOptions(opts?.ChannelOptions));

    public ChannelReader<NatsMsg> Msgs => _msgs.Reader;

    internal static BoundedChannelOptions GetChannelOptions(
        NatsSubChannelOpts? subChannelOpts)
    {
        if (subChannelOpts != null)
        {
            var overrideOpts = subChannelOpts.Value;
            return new BoundedChannelOptions(overrideOpts.Capacity ??
                                             DefaultChannelOptions.Capacity)
            {
                AllowSynchronousContinuations =
                    DefaultChannelOptions.AllowSynchronousContinuations,
                FullMode =
                    overrideOpts.FullMode ?? DefaultChannelOptions.FullMode,
                SingleWriter = DefaultChannelOptions.SingleWriter,
                SingleReader = DefaultChannelOptions.SingleReader,
            };
        }
        else
        {
            return DefaultChannelOptions;
        }
    }

    protected override async ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        ResetIdleTimeout();

        var natsMsg = NatsMsg.Build(
            subject,
            replyTo,
            headersBuffer,
            payloadBuffer,
            Connection,
            Connection.HeaderParser);

        await _msgs.Writer.WriteAsync(natsMsg).ConfigureAwait(false);

        DecrementMaxMsgs();
    }

    protected override void TryComplete() => _msgs.Writer.TryComplete();
}

public sealed class NatsSub<T> : NatsSubBase
{
    private readonly Channel<NatsMsg<T?>> _msgs;

    internal NatsSub(
        NatsConnection connection,
        ISubscriptionManager manager,
        string subject,
        NatsSubOpts? opts,
        INatsSerializer serializer)
        : base(connection, manager, subject, opts)
    {
        _msgs = Channel.CreateBounded<NatsMsg<T?>>(
            NatsSub.GetChannelOptions(opts?.ChannelOptions));

        Serializer = serializer;
    }

    public ChannelReader<NatsMsg<T?>> Msgs => _msgs.Reader;

    private INatsSerializer Serializer { get; }

    protected override async ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        ResetIdleTimeout();

        // We are not handling exceptions here, where there is a possibility of
        // deserialization exceptions. Currently only way for a user to find out is
        // to check the logs created by the client. If the logger isn't hooked up
        // they would be quietly ignored and the message would be lost either way.
        try
        {
            var natsMsg = NatsMsg<T?>.Build(
                subject,
                replyTo,
                headersBuffer,
                payloadBuffer,
                Connection,
                Connection.HeaderParser,
                Serializer);

            await _msgs.Writer.WriteAsync(natsMsg).ConfigureAwait(false);

            DecrementMaxMsgs();
        }
        catch (Exception e)
        {
            var payload = new Memory<byte>(new byte[payloadBuffer.Length]);
            payloadBuffer.CopyTo(payload.Span);

            Memory<byte> headers = default;
            if (headersBuffer != null)
            {
                headers = new Memory<byte>(new byte[headersBuffer.Value.Length]);
            }

            SetException(new NatsSubException($"Message error: {e.Message}", e, payload, headers));
        }
    }

    protected override void TryComplete() => _msgs.Writer.TryComplete();
}

public class NatsSubException : NatsException
{
    public NatsSubException(string message, Exception exception, Memory<byte> payload, Memory<byte> headers)
        : base(message, exception)
    {
        Payload = payload;
        Headers = headers;
    }

    public Memory<byte> Payload { get; }

    public Memory<byte> Headers { get; }
}
