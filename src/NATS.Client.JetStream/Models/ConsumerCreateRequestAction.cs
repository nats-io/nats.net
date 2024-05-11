using System.Runtime.Serialization;

namespace NATS.Client.JetStream.Models;

public enum ConsumerCreateRequestAction
{
    Create = 0,
    Update = 1,
    [EnumMember(Value = "")]
    CreateOrUpdate = 2,
}
