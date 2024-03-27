namespace NATS.Client.Services;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class NatsServiceEndpointAttribute(string name, string group = null) : Attribute
{
    public string Name { get; } = name;
    public string Group { get; } = group;
}
