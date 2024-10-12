using System.Buffers;
using System.Text.Json;
using NATS.Client.Core;

namespace NATS.Client.JetStream.Internal;

internal sealed class NatsJSForcedJsonDocumentSerializer<T> : INatsDeserialize<T>
{
    public static readonly NatsJSForcedJsonDocumentSerializer<T> Default = new();

    public T? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return default;
        }

        // Force return JsonDocument instead of T
        return (T)(object)JsonDocument.Parse(buffer);
    }
}
