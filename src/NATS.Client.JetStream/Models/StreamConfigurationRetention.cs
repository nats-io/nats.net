namespace NATS.Client.JetStream.Models;

public enum StreamConfigurationRetention
{
    [System.Runtime.Serialization.EnumMember(Value = @"limits")]
    Limits = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"interest")]
    Interest = 1,

    [System.Runtime.Serialization.EnumMember(Value = @"workqueue")]
    Workqueue = 2,
}
