using System.Text.Json.Serialization;

namespace NATS.Extensions.Microsoft.DependencyInjection.Tests;

[JsonSerializable(typeof(MyData))]
internal partial class MyJsonContext : JsonSerializerContext;

public record MyData
{
    public MyData(string name) => Name = name;

    public string Name { get; set; }
}
