namespace NATS.Client.JetStream.Models;

public enum StreamConfigurationCompression
{
    [System.Runtime.Serialization.EnumMember(Value = @"none")]
    None = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"s2")]
    S2 = 1,
}
