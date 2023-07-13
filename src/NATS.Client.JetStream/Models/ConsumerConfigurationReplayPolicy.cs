namespace NATS.Client.JetStream.Models;

public enum ConsumerConfigurationReplayPolicy
{
    [System.Runtime.Serialization.EnumMember(Value = @"instant")]
    Instant = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"original")]
    Original = 1,
}
