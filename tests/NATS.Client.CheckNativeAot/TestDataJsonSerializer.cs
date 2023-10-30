using System.Text.Json.Serialization;
using NATS.Client.Core;

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
