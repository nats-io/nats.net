using System.Buffers;
using NATS.Client.Core;

namespace NATS.Client.CheckAbiTransientLib;

/// <summary>
/// This class is compiled against NATS.Net 2.6.0 where INatsSerializer lives in NATS.Client.Core.
/// When used with NATS.Net 2.7.0+, type forwarding should redirect to NATS.Client.Abstractions.
/// </summary>
public class MySerializer : INatsSerializer<string>
{
    public static string GetSerializerInterfaceAssembly()
    {
        return typeof(INatsSerialize<>).Assembly.GetName().Name!;
    }

    public void Serialize(IBufferWriter<byte> bufferWriter, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        bufferWriter.Write(bytes);
    }

    public string? Deserialize(in ReadOnlySequence<byte> buffer)
    {
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public INatsSerializer<string> CombineWith(INatsSerializer<string> next) => this;
}
