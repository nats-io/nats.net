using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
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
        serializer.Serialize(bufferWriter, obj, default);

        // Deserialize back
        var buffer = new ReadOnlySequence<byte>(bufferWriter.WrittenMemory);
        var result = serializer.Deserialize(buffer, default);

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
        var result = serializer.Deserialize(buffer, default);

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
        serializer.Serialize(bufferWriter, obj, default);
        var json = Encoding.UTF8.GetString(bufferWriter.WrittenSpan);

        // With default options, null is included in JSON
        Assert.Contains("null", json);

        // Deserialize back
        var buffer = new ReadOnlySequence<byte>(bufferWriter.WrittenMemory);
        var result = serializer.Deserialize(buffer, default);

        // Assert - Round trip succeeds
        Assert.NotNull(result);
        Assert.Null(result.Name);
    }

    [Fact]
    public void Serialize_WithoutWriterOptions_EscapesNonAsciiByDefault()
    {
        var serializer = new NatsJsonSerializer<Payload>(new JsonSerializerOptions());

        var json = Serialize(serializer, new Payload { Name = "café" });

        Assert.DoesNotContain("café", json);
        Assert.Contains("00E9", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_WithoutWriterOptions_UsesEncoderFromSerializerOptions()
    {
        // The encoder on JsonSerializerOptions is not picked up by Utf8JsonWriter on its own,
        // so it is forwarded to the writer options. See
        // https://github.com/nats-io/nats.net/issues/1219
        var opts = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var serializer = new NatsJsonSerializer<Payload>(opts);

        var json = Serialize(serializer, new Payload { Name = "café" });

        Assert.Equal("""{"Name":"café"}""", json);
    }

    [Fact]
    public void Serialize_WithWriterOptions_UsesGivenEncoder()
    {
        var writerOpts = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true };
        var serializer = new NatsJsonSerializer<Payload>(new JsonSerializerOptions(), writerOpts);

        var json = Serialize(serializer, new Payload { Name = "café" });

        Assert.Equal("""{"Name":"café"}""", json);
    }

    [Fact]
    public void Serialize_WithWriterOptions_HonoursOptionsOnEveryCall()
    {
        var writerOpts = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true };
        var serializer = new NatsJsonSerializer<Payload>(new JsonSerializerOptions(), writerOpts);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("""{"Name":"café"}""", Serialize(serializer, new Payload { Name = "café" }));
        }
    }

    [Fact]
    public void Serialize_WithWriterOptions_UsesGivenIndentation()
    {
        var serializer = new NatsJsonSerializer<Payload>(new JsonSerializerOptions(), new JsonWriterOptions { Indented = true, SkipValidation = true });

        Assert.Contains("\n", Serialize(serializer, new Payload { Name = "a" }));

        // The default writer options stay compact
        Assert.Equal("""{"Name":"a"}""", Serialize(new NatsJsonSerializer<Payload>(new JsonSerializerOptions()), new Payload { Name = "a" }));
    }

    [Fact]
    public void Serialize_WithWriterOptions_IsNotAffectedByOtherInstances()
    {
        // Each serializer must honour its own writer options, whichever ran first, so writer
        // state must never be shared between instances.
        var defaultSerializer = new NatsJsonSerializer<Payload>(new JsonSerializerOptions());
        var relaxedSerializer = new NatsJsonSerializer<Payload>(
            new JsonSerializerOptions(),
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true });

        var obj = new Payload { Name = "café" };

        Assert.DoesNotContain("café", Serialize(defaultSerializer, obj));
        Assert.Equal("""{"Name":"café"}""", Serialize(relaxedSerializer, obj));
        Assert.DoesNotContain("café", Serialize(defaultSerializer, obj));
    }

    private static string Serialize<T>(NatsJsonSerializer<T> serializer, T value)
    {
        var bufferWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(bufferWriter, value, default);
        return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
    }

    private class TestObjectWithOptionalNullable
    {
        public string? Name { get; init; }
    }

    private class TestObjectWithRequiredNullable
    {
        public required string? Name { get; init; }
    }

    private class Payload
    {
        public string? Name { get; init; }
    }
}
#endif
