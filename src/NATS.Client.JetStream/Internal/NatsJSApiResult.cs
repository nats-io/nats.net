using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal readonly struct NatsJSApiResult<T>
{
    private readonly T? _value;
    private readonly ApiError? _error;
    private readonly Exception? _exception;

    public NatsJSApiResult(T value)
    {
        _value = value;
        _error = null;
        _exception = null;
    }

    public NatsJSApiResult(ApiError error)
    {
        _value = default;
        _error = error;
        _exception = null;
    }

    public NatsJSApiResult(Exception exception)
    {
        _value = default;
        _error = null;
        _exception = exception;
    }

    public T Value => _value ?? ThrowValueIsNotSetException();

    public ApiError Error => _error ?? ThrowErrorIsNotSetException();

    public Exception Exception => _exception ?? ThrowExceptionIsNotSetException();

    public bool Success => _error == null && _exception == null;

    public bool HasError => _error != null;

    public bool HasException => _exception != null;

    public static implicit operator NatsJSApiResult<T>(T value) => new(value);

    public static implicit operator NatsJSApiResult<T>(ApiError error) => new(error);

    public static implicit operator NatsJSApiResult<T>(Exception exception) => new(exception);

    private static T ThrowValueIsNotSetException() => throw CreateInvalidOperationException("Result value is not set");

    private static ApiError ThrowErrorIsNotSetException() => throw CreateInvalidOperationException("Result error is not set");

    private static Exception ThrowExceptionIsNotSetException() => throw CreateInvalidOperationException("Result exception is not set");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception CreateInvalidOperationException(string message) => new InvalidOperationException(message);
}
