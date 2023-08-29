using System.Text;

namespace NATS.Client.Core;

public class RawData
{
    public RawData(byte[] buffer) => Buffer = buffer;

    public byte[] Buffer { get; }

    public override string ToString() => Encoding.ASCII.GetString(Buffer);
}
