namespace NATS.Client.JetStream.Models;

// TODO: enum member naming with JSON serialization isn't working for some reason
#pragma warning disable SA1300
public enum ConsumerConfigurationAckPolicy
{
    [System.Runtime.Serialization.EnumMember(Value = @"none")]
    none = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"all")]
    all = 1,

    [System.Runtime.Serialization.EnumMember(Value = @"explicit")]
    @explicit = 2,
}
