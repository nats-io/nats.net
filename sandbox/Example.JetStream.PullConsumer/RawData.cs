using System.Text;

namespace Example.JetStream.PullConsumer;

public class RawData
{
    public RawData(byte[] buffer) => Buffer = buffer;

    public byte[] Buffer { get; }

    public override string ToString() => Encoding.ASCII.GetString(Buffer);
}
