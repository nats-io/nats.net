using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSStream
{
    private readonly NatsJSContext _context;
    private readonly StreamInfo _info;
    private readonly string _name;

    public NatsJSStream(NatsJSContext context, StreamInfo info)
    {
        _context = context;
        _info = info;
        _name = info.Config.Name;
    }
}
