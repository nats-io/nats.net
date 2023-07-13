namespace NATS.Client.JetStream.Models;

public enum StreamConfigurationStorage
{
    [System.Runtime.Serialization.EnumMember(Value = @"file")]
    File = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"memory")]
    Memory = 1,
}
