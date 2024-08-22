using NATS.Client.JetStream.Internal;

namespace NATS.Client.JetStream.Tests;

public class MetadataParserTests
{
    public static IEnumerable<object[]> GetData()
    {
        var expected = new NatsJSMsgMetadata(
            Domain: "domain",
            Stream: "stream",
            Consumer: "cons",
            NumDelivered: 100,
            Sequence: new NatsJSSequencePair(Stream: 200, Consumer: 150),
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(513553500000),
            NumPending: 400);

        var expectedNoDomain = new NatsJSMsgMetadata(
            Domain: string.Empty,
            Stream: "stream",
            Consumer: "cons",
            NumDelivered: 100,
            Sequence: new NatsJSSequencePair(Stream: 200, Consumer: 150),
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(513553500000),
            NumPending: 400);

        return
        [
            [new TestData(Name: "parse v2 successful", Subject: "$JS.ACK.domain.hash-123.stream.cons.100.200.150.513553500000000000.400.token", Metadata: expected)],
            [new TestData(Name: "parse v2 ignore one", Subject: "$JS.ACK.domain.hash-123.stream.cons.100.200.150.513553500000000000.400.token.extra", Metadata: expected)],
            [new TestData(Name: "parse v2 ignore two", Subject: "$JS.ACK.domain.hash-123.stream.cons.100.200.150.513553500000000000.400.token.1.2", Metadata: expected)],
            [new TestData(Name: "parse v2 underscore", Subject: "$JS.ACK._.hash-123.stream.cons.100.200.150.513553500000000000.400.token", Metadata: expectedNoDomain)],
            [new TestData(Name: "parse v1 successful", Subject: "$JS.ACK.stream.cons.100.200.150.513553500000000000.400", Metadata: expectedNoDomain)],
            [new TestData(Name: "invalid no subject1", Subject: string.Empty, Metadata: null)],
            [new TestData(Name: "invalid no subject2", Subject: null, Metadata: null)],
            [new TestData(Name: "invalid less than 9", Subject: "$JS.ACK.2.3.4.5.6.7", Metadata: null)],
            [new TestData(Name: "invalid in 9 and 11", Subject: "$JS.ACK.2.3.4.5.6.7.8.9", Metadata: null)],
            [new TestData(Name: "invalid subject js1", Subject: "$AB.ACK.stream.cons.100.200.150.513553500000000000.400", Metadata: null)],
            [new TestData(Name: "invalid subject js2", Subject: "$ABC.ACK.stream.cons.100.200.150.513553500000000000.400", Metadata: null)],
            [new TestData(Name: "invalid subject js3", Subject: "ABC.ACK.stream.cons.100.200.150.513553500000000000.400", Metadata: null)],
            [new TestData(Name: "invalid subject js4", Subject: "1234.ACK.stream.cons.100.200.150.513553500000000000.400", Metadata: null)],
            [new TestData(Name: "invalid subject ac1", Subject: "$JS.ABC.stream.cons.100.200.150.513553500000000000.400", Metadata: null)],
            [new TestData(Name: "invalid subject ac2", Subject: "$JS.AB.stream.cons.100.200.150.513553500000000000.400", Metadata: null)],
            [new TestData(Name: "invalid subject ac3", Subject: "$JS.1234.stream.cons.100.200.150.513553500000000000.400", Metadata: null)],
        ];
    }

    [Theory]
    [MemberData(nameof(GetData))]
    public void ParseMetadata(TestData data)
    {
        var metadata = ReplyToDateTimeAndSeq.Parse(data.Subject);
        Assert.Equal(data.Metadata, metadata);
    }

    public record TestData(string Name, string? Subject, NatsJSMsgMetadata? Metadata)
    {
        public override string ToString() => Name;
    }
}
