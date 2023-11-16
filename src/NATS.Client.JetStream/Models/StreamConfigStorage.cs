namespace NATS.Client.JetStream.Models;

// TODO: enum member naming with JSON serialization isn't working for some reason
#pragma warning disable SA1300
public enum StreamConfigStorage
{
    [System.Runtime.Serialization.EnumMember(Value = @"file")]
    file = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"memory")]
    memory = 1,
}
