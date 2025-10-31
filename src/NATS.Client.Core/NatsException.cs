using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public class NatsException : Exception
{
    public NatsException(string message)
        : base(message)
    {
    }

    public NatsException(string message, Exception exception)
        : base(message, exception)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Throw(string message) => throw new NatsException(message);
}

public sealed class NatsNoReplyException : NatsException
{
    public NatsNoReplyException()
        : base("No reply received")
    {
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Throw() => throw new NatsNoReplyException();
}

public sealed class NatsNoRespondersException : NatsException
{
    public NatsNoRespondersException()
        : base("No responders")
    {
    }
}

public sealed class NatsServerException : NatsException
{
    public NatsServerException(string error)
        : base($"Server error: {error}")
    {
        Error = error.ToLower();
        IsAuthError = Error.Contains("authorization violation")
                      || Error.Contains("user authentication expired")
                      || Error.Contains("user authentication revoked")
                      || Error.Contains("account authentication expired");
    }

    public string Error { get; }

    public bool IsAuthError { get; }
}

public sealed class NatsPayloadTooLargeException : NatsException
{
    public NatsPayloadTooLargeException(string error)
        : base(error)
    {
    }
}

public sealed class NatsConnectionFailedException(string message) : NatsException(message);
