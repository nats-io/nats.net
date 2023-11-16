namespace NATS.Client.JetStream.Models;

// TODO: enum member naming with JSON serialization isn't working for some reason
#pragma warning disable SA1300
public enum StreamConfigCompression
{
    [System.Runtime.Serialization.EnumMember(Value = @"none")]
    none = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"s2")]
    s2 = 1,
}
