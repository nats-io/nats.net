namespace NATS.Client.Core.Tests.Internal;

public class ReplyToDateTimeAndSeqTest
{
    [Fact]
    public void ShouldParseReplyToDateTimeAndSeq()
    {
        var (dateTime, seq) = ReplyToDateTimeAndSeq.Parse("$JS.ACK.UnitTest.GetEvents_0.1.100.1.1696023331771188000.0");

        dateTime.ToString("O").Should().Be("2023-09-29T21:35:31.7710000+00:00");
        seq.Should().Be(100);
    }
}
