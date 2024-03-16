namespace NATS.Client.Services;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ServiceEndpointAttribute : Attribute
{
    public string Name { get; }
    public string Group { get; }

    public ServiceEndpointAttribute(string name, string group = null)
    {
        Name = name;
        Group = group;
    }
}
