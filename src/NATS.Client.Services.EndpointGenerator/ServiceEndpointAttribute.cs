namespace NATS.Client.Services.EndpointGenerator;

[AttributeUsage(AttributeTargets.Method)]
public class ServiceEndpointAttribute(string name, string? queueGroup = null) : Attribute
{
    public string Name { get; } = name;
    public string? QueueGroup { get; } = queueGroup;
}
