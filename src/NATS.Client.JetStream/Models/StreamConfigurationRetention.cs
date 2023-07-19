namespace NATS.Client.JetStream.Models;

// TODO: enum member naming with JSON serialization isn't working for some reason
#pragma warning disable SA1300
public enum StreamConfigurationRetention
{
    [System.Runtime.Serialization.EnumMember(Value = @"limits")]
    limits = 0,

    [System.Runtime.Serialization.EnumMember(Value = @"interest")]
    interest = 1,

    [System.Runtime.Serialization.EnumMember(Value = @"workqueue")]
    workqueue = 2,
}
