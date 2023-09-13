using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core.Commands;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public enum NatsSubEndReason
{
    None,
    MaxMsgs,
    MaxBytes,
    Timeout,
    IdleTimeout,
    IdleHeartbeatTimeout,
    StartUpTimeout,
    Exception,
    JetStreamError,
}

public abstract class NatsSubBase
{
    private readonly ILogger _logger;
    private readonly bool _debug;
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
    private Exception? _exception;

    internal NatsSubBase(
        NatsConnection connection,
        ISubscriptionManager manager,
        string subject,
        string? queueGroup,
        NatsSubOpts? opts)
    {
        _logger = connection.Opts.LoggerFactory.CreateLogger<NatsSubBase>();
        _debug = _logger.IsEnabled(LogLevel.Debug);
        _manager = manager;
        _pendingMsgs = opts is { MaxMsgs: > 0 } ? opts.MaxMsgs ?? -1 : -1;
        _countPendingMsgs = _pendingMsgs > 0;
        _idleTimeout = opts?.IdleTimeout ?? default;
        _startUpTimeout = opts?.StartUpTimeout ?? default;
        _timeout = opts?.Timeout ?? default;

        Connection = connection;
        Subject = subject;
        QueueGroup = queueGroup;

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
    public int? PendingMsgs => _pendingMsgs == -1 ? null : Volatile.Read(ref _pendingMsgs);

    public NatsSubEndReason EndReason => (NatsSubEndReason)Volatile.Read(ref _endReasonRaw);

    protected NatsConnection Connection { get; }

    public virtual ValueTask ReadyAsync()
    {
        // Let idle timer start with the first message, in case
        // we're allowed to wait longer for the first message.
        if (_startUpTimeoutTimer == null)
            _idleTimeoutTimer?.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

        _startUpTimeoutTimer?.Change(_startUpTimeout, Timeout.InfiniteTimeSpan);
        _timeoutTimer?.Change(dueTime: _timeout, period: Timeout.InfiniteTimeSpan);

        return ValueTask.CompletedTask;
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

    public virtual ValueTask DisposeAsync()
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
        {
            if (Exception is NatsSubException { Exception: not null } nse)
            {
                nse.Exception.Throw();
            }

            throw Exception;
        }

        return unsubscribeAsync;
    }

    public virtual async ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
    {
        ResetIdleTimeout();

        try
        {
            // Need to await to handle any exceptions
            await ReceiveInternalAsync(subject, replyTo, headersBuffer, payloadBuffer).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // When user disposes or unsubscribes there maybe be messages still coming in
            // (e.g. JetStream consumer batch might not be finished) even though we're not
            // interested in the messages anymore. Hence we ignore any messages being
            // fed into the channel and rejected.
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

            SetException(new NatsSubException($"Message error: {e.Message}", ExceptionDispatchInfo.Capture(e), payload, headers));
        }
    }

    internal void ClearException() => Interlocked.Exchange(ref _exception, null);

    /// <summary>
    /// Collect commands when reconnecting.
    /// </summary>
    /// <remarks>
    /// By default this will yield the required subscription command.
    /// When overriden base must be called to yield the re-subscription command.
    /// Additional command (e.g. publishing pull requests in case of JetStream consumers) can be yielded as part of the reconnect routine.
    /// </remarks>
    /// <param name="sid">SID which might be required to create subscription commands</param>
    /// <returns>IEnumerable list of commands</returns>
    internal virtual IEnumerable<ICommand> GetReconnectCommands(int sid)
    {
        yield return AsyncSubscribeCommand.Create(Connection.ObjectPool, Connection.GetCancellationTimer(default), sid, Subject, QueueGroup, PendingMsgs);
    }

    /// <summary>
    /// Invoked when a MSG or HMSG arrives for the subscription.
    /// <remarks>
    /// This method is invoked while reading from the socket. Buffers belong to the socket reader and you should process them as quickly as possible or create a copy before you return from this method.
    /// </remarks>
    /// </summary>
    /// <param name="subject">Subject received for this subscription. This might not be the subject you subscribed to especially when using wildcards. For example, if you subscribed to events.* you may receive events.open.</param>
    /// <param name="replyTo">Subject the sender wants you to send messages back to it.</param>
    /// <param name="headersBuffer">Raw headers bytes. You can use <see cref="NatsConnection"/> <see cref="NatsHeaderParser"/> to decode them.</param>
    /// <param name="payloadBuffer">Raw payload bytes.</param>
    /// <returns></returns>
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

    /// <summary>
    /// Invoked to signal end of the subscription.
    /// </summary>
    protected abstract void TryComplete();

    protected void EndSubscription(NatsSubEndReason reason)
    {
        if (_debug)
            _logger.LogDebug("End subscription {Reason}", reason);

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
#pragma warning disable CA2012
#pragma warning disable VSTHRD110
        UnsubscribeAsync();
#pragma warning restore VSTHRD110
#pragma warning restore CA2012
    }
}
