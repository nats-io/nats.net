using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal sealed class NatsJSErrorAwareJsonSerializer<T> : INatsSerializer<T>
{
    public static readonly NatsJSErrorAwareJsonSerializer<T> Default = new();

    public void Serialize(IBufferWriter<byte> bufferWriter, T? value) =>
        throw new NotSupportedException();

    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        // We need to determine what type we're deserializing into
        // .NET 6 new APIs to the rescue: we can read the buffer once
        // by deserializing into a document, inspect and using the new
        // API deserialize to the final type from the document.
        var jsonDocument = JsonDocument.Parse(buffer);
        if (jsonDocument.RootElement.TryGetProperty("error", out var errorElement))
        {
            var error = errorElement.Deserialize(NatsJSJsonSerializerContext.Default.ApiError) ?? throw new NatsJSException("Can't parse JetStream error JSON payload");
            throw new NatsJSApiErrorException(error);
        }

        return NatsJSJsonSerializer<T>.Default.Deserialize(buffer);
    }
}

internal class NatsJSApiErrorException : Exception
{
    public NatsJSApiErrorException(ApiError error) => Error = error;

    public ApiError Error { get; }
}
