using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSApiException : NatsJSException
{
    public NatsJSApiException(ApiError error)
        : base(error.Description) =>
        Error = error;

    public ApiError Error { get; }
}
