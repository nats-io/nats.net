using System.Text.Json.Serialization;

namespace NATS.Extensions.Microsoft.DependencyInjection.Tests;

[JsonSerializable(typeof(MyData))]
internal partial class MyJsonContext : JsonSerializerContext;

public record MyData(string Name);
