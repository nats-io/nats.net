using NATS.Client.Core;

namespace NATS.Client.Services.Controllers;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class NatsServiceEndpointAttribute : Attribute
{
    public string Name { get; }
    public string Subject { get; }
    public string QueueGroup { get; }
    public IDictionary<string, string> Metadata { get; }
    public Type SerializerType { get; }

    public NatsServiceEndpointAttribute(
        string name,
        string? subject = default,
        string? queueGroup = default,
        IDictionary<string, string>? metadata = default,
        Type? serializerType = default)
    {
        Name = name;
        Subject = subject ?? name;
        QueueGroup = queueGroup ?? "q";
        Metadata = metadata ?? new Dictionary<string, string>();
        SerializerType = serializerType ?? typeof(INatsDefaultSerializer);
    }

    public INatsDeserialize<T> GetSerializer<T>()
    {
        if (SerializerType == typeof(INatsDefaultSerializer))
        {
            return new NatsDefaultSerializer<T>();
        }
        else
        {
            return (INatsDeserialize<T>)Activator.CreateInstance(SerializerType);
        }
    }
}
