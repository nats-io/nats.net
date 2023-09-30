using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Tests.Internal;

public class ReplyToDateTimeAndSeqTest
{
    [Fact]
    public void ShouldParseReplyToDateTimeAndSeq()
    {
        var (dateTime, seq) = ReplyToDateTimeAndSeq.Parse("$JS.ACK.UnitTest.GetEvents_0.1.100.1.1696023331771188000.0");

        dateTime!.Value.ToString("O").Should().Be("2023-09-29T21:35:31.7710000+00:00");
        seq.Should().Be(100);
    }

    [Fact]
    public void ShouldSetNullForReturnWhenReplyToIsNull()
    {
        var (dateTime, seq) = ReplyToDateTimeAndSeq.Parse(null);

        dateTime.Should().BeNull();
        seq.Should().BeNull();
    }

    [Fact]
    public void ShouldSetNullWhenReplyToIsASimpleReqResponse()
    {
        var (dateTime, seq) = ReplyToDateTimeAndSeq.Parse("_INBOX.1");

        dateTime.Should().BeNull();
        seq.Should().BeNull();
    }
}
