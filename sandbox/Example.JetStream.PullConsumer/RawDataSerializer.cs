using System.Buffers;
using NATS.Client.Core;

namespace Example.JetStream.PullConsumer;

public class RawDataSerializer : INatsSerializer
{
    public INatsSerializer? Next => default;

    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        if (value is RawData data)
        {
            bufferWriter.Write(data.Buffer);
            return data.Buffer.Length;
        }

        throw new Exception($"Can only work with '{typeof(RawData)}'");
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer)
    {
        if (typeof(T) != typeof(RawData))
            throw new Exception($"Can only work with '{typeof(RawData)}'");

        return (T)(object)new RawData(buffer.ToArray());
    }
}
