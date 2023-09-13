using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public static class NatsJSExtensions
{
    /// <summary>
    /// Make sure acknowledgment was successful and throw an exception otherwise.
    /// </summary>
    /// <param name="ack">ACK response.</param>
    /// <exception cref="ArgumentNullException"><see cref="PubAckResponse"/> is <c>NULL</c>.</exception>
    /// <exception cref="NatsJSApiException">Server responded with an error.</exception>
    /// <exception cref="NatsJSDuplicateMessageException">A message with the same <c>Nats-Msg-Id</c> was received before.</exception>
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
