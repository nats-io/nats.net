namespace NATS.Client.JetStream.Models;

public enum ConsumerConfigAckPolicy
{
    Explicit = 0,
    All = 1,
    None = 2,

    /// <summary>
    /// Acks based on flow control responses. Used for durable consumers driving mirror or source replication (server 2.14+).
    /// </summary>
    FlowControl = 3,
}
