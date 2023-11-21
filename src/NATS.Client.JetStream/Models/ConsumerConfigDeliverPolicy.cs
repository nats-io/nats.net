namespace NATS.Client.JetStream.Models;

public enum ConsumerConfigDeliverPolicy
{
    All = 0,
    Last = 1,
    New = 2,
    ByStartSequence = 3,
    ByStartTime = 4,
    LastPerSubject = 5,
}
