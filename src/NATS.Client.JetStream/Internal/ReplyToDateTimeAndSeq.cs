namespace NATS.Client.JetStream.Internal;

internal static class ReplyToDateTimeAndSeq
{
    internal static (DateTimeOffset? DateTime, long? Seq) Parse(string? reply)
    {
        if (reply == null)
        {
            return (null, null);
        }

        var ackSeperated = reply.Split(".");

        // It must be seperated by 9 dots as defined in
        // https://docs.nats.io/reference/reference-protocols/nats_api_reference#acknowledging-messages
        if (ackSeperated.Length != 9)
        {
            return (null, null);
        }

        var timestamp = long.Parse(ackSeperated[^2]);
        var offset = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1000000);
        var dateTime = new DateTimeOffset(offset.Ticks, TimeSpan.Zero);
        var seq = long.Parse(ackSeperated[5]);

        return (dateTime, seq);
    }
}
