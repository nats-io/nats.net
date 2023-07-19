namespace NATS.Client.JetStream.Models;

// TODO: enum member naming with JSON serialization isn't working for some reason
#pragma warning disable SA1300
public enum ConsumerConfigurationReplayPolicy
{
    [System.Runtime.Serialization.EnumMember(Value = @"instant")]
    instant = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"original")]
    original = 1,
}
