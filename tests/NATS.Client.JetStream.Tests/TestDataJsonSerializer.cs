using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Tests;

public static class TestDataJsonSerializer
{
    public static readonly INatsSerializer Default = new NatsJsonContextSerializer(TestDataJsonSerializerContext.Default);
}

public record TestData
{
    public int Test { get; set; }
}

[JsonSerializable(typeof(TestData))]
public partial class TestDataJsonSerializerContext : JsonSerializerContext
{
}
