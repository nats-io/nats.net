using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;

namespace NATS.Client.JetStream.Internal;

internal sealed class NatsJSJsonDocumentSerializer : INatsDeserialize<JsonDocument>
{
    public static readonly NatsJSJsonDocumentSerializer Default = new();

    public JsonDocument? Deserialize(in ReadOnlySequence<byte> buffer) => buffer.Length == 0 ? default : JsonDocument.Parse(buffer);
}
