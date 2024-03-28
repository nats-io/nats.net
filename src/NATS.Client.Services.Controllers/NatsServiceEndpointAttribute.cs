namespace NATS.Client.Services.Controllers;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class NatsServiceEndpointAttribute(
    string name,
    string? subject = default,
    string? queueGroup = default) : Attribute
{
    public string Name { get; } = name;
    public string Subject { get; } = subject ?? name;
    public string QueueGroup { get; } = queueGroup ?? "q";
}
