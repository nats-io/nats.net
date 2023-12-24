using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;


public struct CheatingPeeker<TResult>
{
    internal readonly object? _obj;
    /// <summary>The result to be used if the operation completed successfully synchronously.</summary>
    internal readonly TResult? _result;
    /// <summary>Opaque value passed through to the <see cref="IValueTaskSource{TResult}"/>.</summary>
    internal readonly short _token;
}
internal static class PVTSSentinels<TResponse>
{

}
public sealed class PooledValueTaskSource<TResponse> :
    IValueTaskSource<NatsMsg<TResponse>>
{
    private INatsConnection? _connection;
    private ManualResetValueTaskSourceCore<NatsMsg<TResponse>> _core;
    private ValueTask<InFlightNatsMsg<TResponse>> _source;
    private int _continuationFlags;
    private SubWrappedChannelReader<TResponse>? _borrowedFor;

    internal PooledValueTaskSource()
    {
    }

    internal ValueTask<NatsMsg<TResponse>> ToValueTaskAsync(ValueTask<InFlightNatsMsg<TResponse>> msg, INatsConnection? connection)
    {
        _connection = connection;
        _source = msg;

        return new ValueTask<NatsMsg<TResponse>>(this, _core.Version);
    }

    private void InternalOnCompleted()
    {
        try
        {
#pragma warning disable VSTHRD002
            _continuationFlags = _continuationFlags | 3;
            _core.SetResult(_source.GetAwaiter().GetResult().ToNatsMsg(_connection));
#pragma warning restore VSTHRD002
        }
        catch (Exception e)
        {
            SetException(e);
        }
    }

    public void SetException(Exception exception)
    {
        if (_continuationFlags > 3)
            return;
        _continuationFlags = _continuationFlags | 8;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                state.self._core.SetException(state.exception);
            },
            (self: this, exception),
            preferLocal: false);
    }

    NatsMsg<TResponse> IValueTaskSource<NatsMsg<TResponse>>.GetResult(short token)
    {
        if (_core.Version == token && (_continuationFlags & 4) != 0)
        {
            var peek = Unsafe.As<ValueTask<InFlightNatsMsg<TResponse>>, CheatingPeeker<InFlightNatsMsg<TResponse>>>(ref _source);
            if (peek._obj is IValueTaskSource<InFlightNatsMsg<TResponse>> result)
            {
                var res = result.GetResult(peek._token).ToNatsMsg(_connection);
                ResetAndTryReturn();
                return res;
            }
            else if (peek._obj == null)
            {
                var res = peek._result.ToNatsMsg(_connection);
                ResetAndTryReturn();
                return res;
            }
            else
            {
                var res = ((Task<InFlightNatsMsg<TResponse>>)peek._obj).Result.ToNatsMsg(_connection);
                ResetAndTryReturn();
                return res;
            }

            //var res = _source.Result.ToNatsMsg(_connection);
            //ResetAndTryReturn();
            //return res;
        }
        else
        {
            return getResultSlow(token);
        }
    }

    private NatsMsg<TResponse> getResultSlow(short token)
    {
        try
        {
            if ((_continuationFlags & 1) == 0)
            {
                if (GetStatus(token) == ValueTaskSourceStatus.Succeeded &&
                    _core.GetStatus(token) == ValueTaskSourceStatus.Pending)
                {
                    return _source.Result.ToNatsMsg(_connection);
                }
            }

            return _core.GetResult(token);
        }
        finally
        {
            ResetAndTryReturn();
        }
    }

    private void ResetAndTryReturn()
    {
        _core.Reset();
        _source = default;
        //var p = _objectPool;
        //_objectPool = null;
        // canceled object don't return pool to avoid call SetResult/Exception after await
        //if (p != null && !_noReturn)
        if (_continuationFlags < 7)
        {
            //var b = _borrowedFor;
            _continuationFlags = 0;
            if (_borrowedFor is { _internalPooledSource: null })
            {
                _borrowedFor._internalPooledSource = this;
                //var other = Interlocked.Exchange(ref _borrowedFor._internalPooledSource!, this);
            }
            else
            {
                _borrowedFor = null;
                if (_pool.Count < 128)
                {
                    _pool.Enqueue(this);
                }
            }
        }
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        //var status = _core.GetStatus(token);
        if ((_continuationFlags & 4) != 0)
        {
            return ValueTaskSourceStatus.Succeeded;
        }
        else if (_source.IsCompletedSuccessfully && _continuationFlags == 0)// && status == ValueTaskSourceStatus.Pending)
        {
#pragma warning disable VSTHRD002
            _continuationFlags = 4;
            //_core.SetResult(_source.GetAwaiter().GetResult().ToNatsMsg(_connection));
#pragma warning restore VSTHRD002
            return ValueTaskSourceStatus.Succeeded;
        }
        else if (Interlocked.CompareExchange(ref _continuationFlags, 1, 0) == 0)
        {
            return QueueContinuation();
        }
        else
        {
            return _core.GetStatus(token);
        }
    }

    private ValueTaskSourceStatus QueueContinuation()
    {
        var peeker =
            Unsafe
                .As<ValueTask<InFlightNatsMsg<TResponse>>,
                    CheatingPeeker<InFlightNatsMsg<TResponse>>>(ref _source);
        if (peeker._obj is IValueTaskSource<InFlightNatsMsg<TResponse>> vts)
        {
            vts.OnCompleted(
                static o =>
                {
                    var me = o as PooledValueTaskSource<TResponse>;
                    me!.InternalOnCompleted();
                },
                this,
                peeker._token,
                ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
        }
        else
        {
            _source.GetAwaiter().OnCompleted(InternalOnCompleted);
        }

        return ValueTaskSourceStatus.Pending;
    }

    void IValueTaskSource<NatsMsg<TResponse>>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static PooledValueTaskSource<TResponse> RentOrGet(SubWrappedChannelReader<TResponse> borrower)
    {
        //var self = Interlocked.Exchange(ref borrower._internalPooledSource, null);
        if (_pool.TryDequeue(out var self) == false)
        {
            self = new PooledValueTaskSource<TResponse>();
        }

        self._borrowedFor = borrower;
        //if (pool == null || pool.TryRent<PooledValueTaskSource<TResponse>>(out var self) == false)
        //{
        //    self = new PooledValueTaskSource<TResponse>();
        //}

        //self._objectPool = pool;

        return self;
    }

    private static readonly ConcurrentQueue<PooledValueTaskSource<TResponse>> _pool = new ConcurrentQueue<PooledValueTaskSource<TResponse>>();
}
