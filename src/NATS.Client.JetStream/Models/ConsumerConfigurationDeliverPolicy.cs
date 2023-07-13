namespace NATS.Client.JetStream.Models;

public enum ConsumerConfigurationDeliverPolicy
{
    [System.Runtime.Serialization.EnumMember(Value = @"all")]
    All = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"last")]
    Last = 1,

    [System.Runtime.Serialization.EnumMember(Value = @"new")]
    New = 2,

    [System.Runtime.Serialization.EnumMember(Value = @"by_start_sequence")]
    ByStartSequence = 3,

    [System.Runtime.Serialization.EnumMember(Value = @"by_start_time")]
    ByStartTime = 4,

    [System.Runtime.Serialization.EnumMember(Value = @"last_per_subject")]
    LastPerSubject = 5,
}
