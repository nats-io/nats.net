namespace NATS.Client.JetStream.Models;

public enum StreamConfigurationDiscard
{
    [System.Runtime.Serialization.EnumMember(Value = @"old")]
    Old = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"new")]
    New = 1,
}
