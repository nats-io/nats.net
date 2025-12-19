using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Serializers.Json;

namespace NATS.Client.CoreUnit.Tests;

#if NET8_0_OR_GREATER
public class NatsJsonSerializerTests
{
    [Fact]
    public void RoundTrip_RequiredNullableProperty_ShouldSucceed()
    {
        // This test demonstrates the issue reported in https://github.com/nats-io/nats.net/issues/974
        // It FAILS with the current implementation because DefaultIgnoreCondition = WhenWritingNull
        // breaks round-tripping for required nullable properties.
        //
        // This test will PASS once the default options are changed to JsonSerializerOptions.Default

        // Arrange
        var serializer = NatsJsonSerializer<TestObjectWithRequiredNullable>.Default;
        var obj = new TestObjectWithRequiredNullable { Name = null };
        var bufferWriter = new ArrayBufferWriter<byte>();

        // Act - Serialize
        serializer.Serialize(bufferWriter, obj);

        // Deserialize back
        var buffer = new ReadOnlySequence<byte>(bufferWriter.WrittenMemory);
        var result = serializer.Deserialize(buffer);

        // Assert - Round trip should succeed
        Assert.NotNull(result);
        Assert.Null(result.Name);
    }

    [Fact]
    public void Deserialize_RequiredNullableProperty_WithJsonSerializerOptionsDefault_Succeeds()
    {
        // Arrange - Using JsonSerializerOptions.Default (without WhenWritingNull)
        var serializer = new NatsJsonSerializer<TestObjectWithRequiredNullable>(JsonSerializerOptions.Default);
        var json = "{\"Name\":null}"u8.ToArray();
        var buffer = new ReadOnlySequence<byte>(json);

        // Act
        var result = serializer.Deserialize(buffer);

        // Assert - This should work fine
        Assert.NotNull(result);
        Assert.Null(result.Name);
    }

    [Fact]
    public void RoundTrip_RequiredNullableProperty_WithJsonSerializerOptionsDefault_Succeeds()
    {
        // Arrange - Using JsonSerializerOptions.Default (without WhenWritingNull)
        var serializer = new NatsJsonSerializer<TestObjectWithRequiredNullable>(JsonSerializerOptions.Default);
        var obj = new TestObjectWithRequiredNullable { Name = null };
        var bufferWriter = new ArrayBufferWriter<byte>();

        // Act - Serialize
        serializer.Serialize(bufferWriter, obj);
        var json = Encoding.UTF8.GetString(bufferWriter.WrittenSpan);

        // With default options, null is included in JSON
        Assert.Contains("null", json);

        // Deserialize back
        var buffer = new ReadOnlySequence<byte>(bufferWriter.WrittenMemory);
        var result = serializer.Deserialize(buffer);

        // Assert - Round trip succeeds
        Assert.NotNull(result);
        Assert.Null(result.Name);
    }

    private class TestObjectWithOptionalNullable
    {
        public string? Name { get; init; }
    }

    private class TestObjectWithRequiredNullable
    {
        public required string? Name { get; init; }
    }
}
#endif
