using System.Text.Json.Serialization;
using NATS.Client.Core;

public static class TestDataJsonSerializer<T>
{
    public static readonly NatsJsonContextSerializer<T> Default = new(TestDataJsonSerializerContext.Default);
}

public record TestData
{
    public int Test { get; set; }
}

[JsonSerializable(typeof(TestData))]
public partial class TestDataJsonSerializerContext : JsonSerializerContext
{
}
