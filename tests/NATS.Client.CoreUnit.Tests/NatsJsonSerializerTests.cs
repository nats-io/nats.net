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
    public void Serialize_WithoutWriterOptions_IgnoresEncoderFromSerializerOptions()
    {
        // The encoder on JsonSerializerOptions is not used when writing to a Utf8JsonWriter,
        // so by default non-ASCII characters are still escaped. See
        // https://github.com/nats-io/nats.net/issues/1219
        var opts = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var serializer = new NatsJsonSerializer<DefaultOptsPayload>(opts);

        var json = Serialize(serializer, new DefaultOptsPayload { Name = "café" });

        Assert.DoesNotContain("café", json);
        Assert.Contains("00E9", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_WithWriterOptions_UsesGivenEncoder()
    {
        var writerOpts = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true };
        var serializer = new NatsJsonSerializer<WriterOptsPayload>(new JsonSerializerOptions(), writerOpts);

        var json = Serialize(serializer, new WriterOptsPayload { Name = "café" });

        Assert.Equal("""{"Name":"café"}""", json);
    }

    [Fact]
    public void Serialize_WithWriterOptions_ReusesInstanceAcrossCalls()
    {
        var writerOpts = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true };
        var serializer = new NatsJsonSerializer<RepeatedWriterOptsPayload>(new JsonSerializerOptions(), writerOpts);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("""{"Name":"café"}""", Serialize(serializer, new RepeatedWriterOptsPayload { Name = "café" }));
        }
    }

    [Fact]
    public void Serialize_WithWriterOptions_UsesGivenIndentation()
    {
        var serializer = new NatsJsonSerializer<IndentedOptsPayload>(new JsonSerializerOptions(), new JsonWriterOptions { Indented = true, SkipValidation = true });

        Assert.Contains("\n", Serialize(serializer, new IndentedOptsPayload { Name = "a" }));

        // The default writer options stay compact
        Assert.Equal("""{"Name":"a"}""", Serialize(new NatsJsonSerializer<IndentedOptsPayload>(new JsonSerializerOptions()), new IndentedOptsPayload { Name = "a" }));
    }

    [Fact]
    public void Serialize_WithWriterOptions_IsNotAffectedByOtherInstancesOnSameThread()
    {
        // NatsJsonSerializer<T> caches a Utf8JsonWriter in a [ThreadStatic] field shared by every
        // instance for a given T, and Utf8JsonWriter.Reset() does not change the writer's options.
        // Each serializer must still honour its own writer options, whichever ran first.
        var defaultSerializer = new NatsJsonSerializer<SharedWriterPayload>(new JsonSerializerOptions());
        var relaxedSerializer = new NatsJsonSerializer<SharedWriterPayload>(
            new JsonSerializerOptions(),
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true });

        var obj = new SharedWriterPayload { Name = "café" };

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

    private class DefaultOptsPayload
    {
        public string? Name { get; init; }
    }

    private class WriterOptsPayload
    {
        public string? Name { get; init; }
    }

    private class RepeatedWriterOptsPayload
    {
        public string? Name { get; init; }
    }

    private class IndentedOptsPayload
    {
        public string? Name { get; init; }
    }

    private class SharedWriterPayload
    {
        public string? Name { get; init; }
    }
}
#endif
