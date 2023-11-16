namespace NATS.Client.JetStream.Models;

// TODO: enum member naming with JSON serialization isn't working for some reason
#pragma warning disable SA1300
public enum ConsumerConfigDeliverPolicy
{
    [System.Runtime.Serialization.EnumMember(Value = @"all")]
    all = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"last")]
    last = 1,

    [System.Runtime.Serialization.EnumMember(Value = @"new")]
    @new = 2,

    [System.Runtime.Serialization.EnumMember(Value = @"by_start_sequence")]
    by_start_sequence = 3,

    [System.Runtime.Serialization.EnumMember(Value = @"by_start_time")]
    by_start_time = 4,

    [System.Runtime.Serialization.EnumMember(Value = @"last_per_subject")]
    last_per_subject = 5,
}
