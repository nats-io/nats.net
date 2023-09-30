using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Tests.Internal;

public class ReplyToDateTimeAndSeqTest
{
    [Fact]
    public void ShouldParseV1ReplyToDateTimeAndSeq()
    {
        var natsJSMsgMetadata = ReplyToDateTimeAndSeq.Parse("$JS.ACK.UnitTest.GetEvents_0.1.100.1.1696023331771188000.0");

        natsJSMsgMetadata!.Value.Timestamp.ToString("O").Should().Be("2023-09-29T21:35:31.7710000+00:00");
        natsJSMsgMetadata.Value.Sequence.Stream.Should().Be(100);
        natsJSMsgMetadata.Value.Sequence.Consumer.Should().Be(1);
        natsJSMsgMetadata.Value.NumDelivered.Should().Be(1);
        natsJSMsgMetadata.Value.NumPending.Should().Be(0);
        natsJSMsgMetadata.Value.Stream.Should().Be("UnitTest");
        natsJSMsgMetadata.Value.Consumer.Should().Be("GetEvents_0");
        natsJSMsgMetadata.Value.Domain.Should().BeEmpty();
    }

    [Fact]
    public void ShouldParseV2ReplyToDateTimeAndSeq()
    {
        var natsJSMsgMetadata = ReplyToDateTimeAndSeq.Parse("$JS.ACK.MyDomain.1234.UnitTest.GetEvents_0.1.100.1.1696023331771188000.0");

        natsJSMsgMetadata!.Value.Timestamp.ToString("O").Should().Be("2023-09-29T21:35:31.7710000+00:00");
        natsJSMsgMetadata.Value.Sequence.Stream.Should().Be(100);
        natsJSMsgMetadata.Value.Sequence.Consumer.Should().Be(1);
        natsJSMsgMetadata.Value.NumDelivered.Should().Be(1);
        natsJSMsgMetadata.Value.NumPending.Should().Be(0);
        natsJSMsgMetadata.Value.Stream.Should().Be("UnitTest");
        natsJSMsgMetadata.Value.Consumer.Should().Be("GetEvents_0");
        natsJSMsgMetadata.Value.Domain.Should().Be("MyDomain");
    }

    [Fact]
    public void ShouldSetNullForReturnWhenReplyToIsNull()
    {
        var natsJSMsgMetadata = ReplyToDateTimeAndSeq.Parse(null);

        natsJSMsgMetadata.Should().BeNull();
    }

    [Fact]
    public void ShouldSetNullWhenReplyToIsASimpleReqResponse()
    {
        var natsJSMsgMetadata = ReplyToDateTimeAndSeq.Parse("_INBOX.1");

        natsJSMsgMetadata.Should().BeNull();
    }

    [Fact]
    public void ShouldSetNullWhenDoesNotStartWithJsAck()
    {
        var natsJSMsgMetadata = ReplyToDateTimeAndSeq.Parse("1.2.3.4.5.6.7.8.9");

        natsJSMsgMetadata.Should().BeNull();
    }
}
