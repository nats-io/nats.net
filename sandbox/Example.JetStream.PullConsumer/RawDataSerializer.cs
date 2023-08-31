using System.Buffers;
using NATS.Client.Core;

namespace Example.JetStream.PullConsumer;

public class RawDataSerializer : INatsSerializer
{
    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        if (value is RawData data)
        {
            bufferWriter.Write(data.Buffer);
            return data.Buffer.Length;
        }

        throw new Exception($"Can only work with '{typeof(RawData)}'");
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer) => (T?)Deserialize(buffer, typeof(T));

    public object? Deserialize(in ReadOnlySequence<byte> buffer, Type type)
    {
        if (type != typeof(RawData))
            throw new Exception($"Can only work with '{typeof(RawData)}'");

        return new RawData(buffer.ToArray());
    }
}
