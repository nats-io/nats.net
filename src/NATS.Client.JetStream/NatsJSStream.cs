using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSStream
{
    private readonly NatsJSContext _context;
    private readonly string _name;

    public NatsJSStream(NatsJSContext context, StreamInfo info)
    {
        _context = context;
        Info = info;
        _name = info.Config.Name;
    }

    public StreamInfo Info { get; }
}
