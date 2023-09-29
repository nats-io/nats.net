namespace NATS.Client.Core.Internal;

internal static class ReplyToDateTimeAndSeq
{
    internal static (DateTimeOffset DateTime, long Seq) Parse(string reply)
    {
        var ackSeperated = reply.Split(".");
        var timestamp = long.Parse(ackSeperated[^2]);
        var offset = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1000000);
        var dateTime = new DateTimeOffset(offset.Ticks, TimeSpan.Zero);
        var seq = long.Parse(ackSeperated[5]);

        return (dateTime, seq);
    }
}
