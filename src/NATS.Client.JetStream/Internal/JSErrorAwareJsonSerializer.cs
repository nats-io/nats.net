using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal sealed class JSErrorAwareJsonSerializer : INatsSerializer
{
    public static readonly JSErrorAwareJsonSerializer Default = new();

    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value) =>
        throw new NotSupportedException();

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        // We need to determine what type we're deserializing into
        // .NET 6 new APIs to the rescue: we can read the buffer once
        // by deserializing into a document, inspect and using the new
        // API deserialize to the final type from the document.
        var jsonDocument = JsonDocument.Parse(buffer);
        if (jsonDocument.RootElement.TryGetProperty("error", out var errorElement))
        {
            var error = errorElement.Deserialize<ApiError>() ?? throw new NatsJSException("Can't parse JetStream error JSON payload");
            throw new JSErrorException(error);
        }

        return jsonDocument.Deserialize<T>();
    }

    public object? Deserialize(in ReadOnlySequence<byte> buffer, Type type) =>
        throw new NotSupportedException();
}

internal class JSErrorException : Exception
{
    public JSErrorException(ApiError error) => Error = error;

    public ApiError Error { get; }
}
