namespace NATS.Client.JetStream.Models;

public enum ConsumerConfigurationAckPolicy
{
    [System.Runtime.Serialization.EnumMember(Value = @"none")]
    None = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"all")]
    All = 1,

    [System.Runtime.Serialization.EnumMember(Value = @"explicit")]
    Explicit = 2,
}
