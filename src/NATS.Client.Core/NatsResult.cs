using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public readonly struct NatsResult<T>
{
    private readonly T? _value;
    private readonly Exception? _error;

    public NatsResult(T value)
    {
        _value = value;
        _error = null;
    }

    public NatsResult(Exception error)
    {
        _value = default;
        _error = error;
    }

    public T Value => _value ?? ThrowValueIsNotSetException();

    public Exception Error => _error ?? ThrowErrorIsNotSetException();

    public bool Success => _error == null;

    public static implicit operator NatsResult<T>(T value) => new(value);

    public static implicit operator NatsResult<T>(Exception error) => new(error);

    private static T ThrowValueIsNotSetException() => throw CreateInvalidOperationException("Result value is not set");

    private static Exception ThrowErrorIsNotSetException() => throw CreateInvalidOperationException("Result error is not set");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception CreateInvalidOperationException(string message) => new InvalidOperationException(message);
}
