using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public static class NatsJSExtensions
{
    public static void EnsureSuccess(this PubAckResponse ack)
    {
        if (ack == null)
            throw new ArgumentNullException(nameof(ack));

        if (ack.Error != null)
            throw new NatsJSApiException(ack.Error);

        if (ack.Duplicate)
            throw new NatsJSDuplicateMessageException(ack.Seq);
    }
}
