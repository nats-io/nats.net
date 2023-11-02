using System.Text.Json.Serialization;
using NATS.Client.Core;

public static class TestDataJsonSerializer<T>
{
    public static readonly INatsSerializer<T> Default = new NatsJsonContextSerializer<T>(TestDataJsonSerializerContext.Default);
}

public record TestData
{
    public int Test { get; set; }
}

[JsonSerializable(typeof(TestData))]
public partial class TestDataJsonSerializerContext : JsonSerializerContext
{
}
