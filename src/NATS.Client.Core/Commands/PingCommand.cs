using System.Threading.Tasks.Sources;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core.Commands;

internal class PingCommand : IValueTaskSource<TimeSpan>, IObjectPoolNode<PingCommand>
{
    private readonly ObjectPool? _pool;
    private DateTimeOffset _start;
    private ManualResetValueTaskSourceCore<TimeSpan> _core;
    private PingCommand? _next;

    public PingCommand(ObjectPool? pool)
    {
        _pool = pool;
        _core = new ManualResetValueTaskSourceCore<TimeSpan>
        {
            RunContinuationsAsynchronously = true,
        };
        _start = DateTimeOffset.MinValue;
    }

    public ref PingCommand? NextNode => ref _next;

    public void Start() => _start = DateTimeOffset.UtcNow;

    public void SetResult() => _core.SetResult(DateTimeOffset.UtcNow - _start);

    public void SetCanceled() => _core.SetException(new OperationCanceledException());

    public void Reset()
    {
        _start = DateTimeOffset.MinValue;
        _core.Reset();
    }

    public ValueTask<TimeSpan> RunAsync() => new(this, _core.Version);

    public TimeSpan GetResult(short token)
    {
        var result = _core.GetResult(token);

        if (_pool is not null)
        {
            Reset();
            _pool.Return(this);
        }

        return result;
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}

internal class RequestCommand : IValueTaskSource<NatsMsg<NatsMemoryOwner<byte>>>, IObjectPoolNode<RequestCommand>
{
    private readonly ObjectPool? _pool;
    private ManualResetValueTaskSourceCore<NatsMsg<NatsMemoryOwner<byte>>> _core;
    private RequestCommand? _next;

    public RequestCommand(ObjectPool? pool)
    {
        _pool = pool;
        _core = new ManualResetValueTaskSourceCore<NatsMsg<NatsMemoryOwner<byte>>>
        {
            RunContinuationsAsynchronously = true,
        };
    }

    public static RequestCommand Rent(ObjectPool pool)
    {
        RequestCommand result;
        if (!pool.TryRent(out result!))
        {
            result = new RequestCommand(pool);
        }

        return result;
    }

    public ref RequestCommand? NextNode => ref _next;

    public void SetResult(NatsMsg<NatsMemoryOwner<byte>> msg) => _core.SetResult(msg);

    public void SetCanceled() => _core.SetException(new OperationCanceledException());

    public void Reset()
    {
        _core.Reset();
    }

    public ValueTask<NatsMsg<NatsMemoryOwner<byte>>> RunAsync() => new(this, _core.Version);

    public NatsMsg<NatsMemoryOwner<byte>> GetResult(short token)
    {
        var result = _core.GetResult(token);

        if (_pool is not null)
        {
            Reset();
            _pool.Return(this);
        }

        return result;
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
