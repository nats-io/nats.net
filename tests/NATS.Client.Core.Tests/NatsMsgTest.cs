using System.Buffers;
using System.Text;

namespace NATS.Client.Core.Tests;

public class NatsMsgTest
{
    [Fact]
    public void Empty_payload()
    {
        var msg1 = NatsMsg<int>.Build(
            subject: "foo",
            replyTo: "bar",
            headersBuffer: null,
            payloadBuffer: new ReadOnlySequence<byte>(new byte[] { }),
            connection: null,
            headerParser: new NatsHeaderParser(Encoding.UTF8),
            NatsDefaultSerializer<int>.Default);
        Assert.True(msg1.HasNoPayload);

        var msg2 = NatsMsg<int>.Build(
            subject: "foo",
            replyTo: "bar",
            headersBuffer: null,
            payloadBuffer: new ReadOnlySequence<byte>(new[] { (byte)'0' }),
            connection: null,
            headerParser: new NatsHeaderParser(Encoding.UTF8),
            NatsDefaultSerializer<int>.Default);
        Assert.False(msg2.HasNoPayload);
    }

    [Fact]
    public void No_responders()
    {
        var msg1 = NatsMsg<int>.Build(
            subject: "foo",
            replyTo: "bar",
            headersBuffer: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("NATS/1.0 503\r\n\r\n")),
            payloadBuffer: new ReadOnlySequence<byte>(new byte[] { }),
            connection: null,
            headerParser: new NatsHeaderParser(Encoding.UTF8),
            NatsDefaultSerializer<int>.Default);
        Assert.True(msg1.IsNoRespondersError);

        var msg2 = NatsMsg<int>.Build(
            subject: "foo",
            replyTo: "bar",
            headersBuffer: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("NATS/1.0 503\r\n\r\n")),
            payloadBuffer: new ReadOnlySequence<byte>(new[] { (byte)'0' }),
            connection: null,
            headerParser: new NatsHeaderParser(Encoding.UTF8),
            NatsDefaultSerializer<int>.Default);
        Assert.False(msg2.IsNoRespondersError);

        var msg3 = NatsMsg<int>.Build(
            subject: "foo",
            replyTo: "bar",
            headersBuffer: new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("NATS/1.0 503\r\nk: v\r\n\r\n")),
            payloadBuffer: new ReadOnlySequence<byte>(new byte[] { }),
            connection: null,
            headerParser: new NatsHeaderParser(Encoding.UTF8),
            NatsDefaultSerializer<int>.Default);
        Assert.False(msg3.IsNoRespondersError);
    }
}
