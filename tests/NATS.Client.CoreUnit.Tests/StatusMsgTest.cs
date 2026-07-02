using System.Buffers;
using System.Text;

namespace NATS.Client.Core.Tests;

public class StatusMsgTest
{
    [Theory]
    [InlineData("NATS/1.0 100 Idle Heartbeat\r\n\r\n")] // JetStream idle heartbeat
    [InlineData("NATS/1.0 503\r\n\r\n")] // no responders
    [InlineData("NATS/1.0 408 Request Timeout\r\n\r\n")] // pull request timeout
    [InlineData("NATS/1.0 409 Message Size Exceeds MaxBytes\r\n\r\n")] // flow control
    [InlineData("NATS/1.0 100 FlowControl Request\r\n\r\n")]
    public void Status_frames_are_detected(string header)
    {
        NatsSubBase.IsStatusMsg(Seq(header)).Should().BeTrue();
    }

    [Theory]
    [InlineData("NATS/1.0\r\nFoo: Bar\r\n\r\n")] // regular user headers
    [InlineData("NATS/1.0\r\n\r\n")] // headers present, no user keys, no status code
    public void User_headers_are_not_status_frames(string header)
    {
        NatsSubBase.IsStatusMsg(Seq(header)).Should().BeFalse();
    }

    [Fact]
    public void Null_or_short_buffer_is_not_a_status_frame()
    {
        NatsSubBase.IsStatusMsg(null).Should().BeFalse();
        NatsSubBase.IsStatusMsg(Seq("NATS/1.0")).Should().BeFalse();
    }

    private static ReadOnlySequence<byte> Seq(string s) => new(Encoding.ASCII.GetBytes(s));
}
