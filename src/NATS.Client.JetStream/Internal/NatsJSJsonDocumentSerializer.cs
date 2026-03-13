using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;

namespace NATS.Client.JetStream.Internal;

internal sealed class NatsJSJsonDocumentSerializer<T> : INatsDeserialize<NatsJSApiResult<T>>
{
    public static readonly NatsJSJsonDocumentSerializer<T> Default = new();

    public NatsJSApiResult<T> Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return new NatsJSException("Buffer is empty");
        }

        using var jsonDocument = JsonDocument.Parse(buffer);

        if (jsonDocument.RootElement.TryGetProperty("error", out var errorElement))
        {
            var error = errorElement.Deserialize(JetStream.NatsJSJsonSerializerContext.Default.ApiError) ?? throw new NatsJSException("Can't parse JetStream error JSON payload");
            return error;
        }

        var jsonTypeInfo = NatsJSJsonSerializerContext.DefaultContext.GetTypeInfo(typeof(T));
        if (jsonTypeInfo == null)
        {
            return new NatsJSException($"Unknown response type {typeof(T)}");
        }

        var result = (T?)jsonDocument.RootElement.Deserialize(jsonTypeInfo);

        if (result == null)
        {
            return new NatsJSException("Null result");
        }

        return result;
    }
}
