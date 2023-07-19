using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// JetStream response including an optional error property encapsulating both successful and failed calls.
/// </summary>
/// <typeparam name="T">JetStream response type</typeparam>
public readonly struct JSResponse<T>
{
    internal JSResponse(T? response, ApiError? error)
    {
        Response = response;
        Error = error;
    }

    public T? Response { get; }

    public ApiError? Error { get; }

    public bool Success => Error == null && Response != null;
}
