namespace NATS.Client.JetStream.Internal;

internal static class ReplyToDateTimeAndSeq
{
    private const int V1TokenCounts = 9;
    private const int V2TokenCounts = 12;

    private const int AckDomainTokenPos = 2;
    private const int AckAccHashTokenPos = 3;
    private const int AckStreamTokenPos = 4;
    private const int AckConsumerTokenPos = 5;
    private const int AckNumDeliveredTokenPos = 6;
    private const int AckStreamSeqTokenPos = 7;
    private const int AckConsumerSeqTokenPos = 8;
    private const int AckTimestampSeqTokenPos = 9;
    private const int AckNumPendingTokenPos = 10;

    internal static NatsJSMsgMetadata? Parse(string? reply)
    {
        if (reply == null)
        {
            return null;
        }

        var originalTokens = reply.Split(".").AsSpan();
        var tokensLength = originalTokens.Length;

        // Parsed based on https://github.com/nats-io/nats-architecture-and-design/blob/main/adr/ADR-15.md#jsack

        // If lower than 9 or more than 9 but less than 11, report an error
        if (tokensLength is < V1TokenCounts or > V1TokenCounts and < V2TokenCounts - 1)
        {
            return null;
        }

        if (originalTokens[0] != "$JS" || originalTokens[1] != "ACK")
        {
            return null;
        }

        var tokens = new string[V2TokenCounts].AsSpan();

        if (tokensLength == V1TokenCounts)
        {
            originalTokens[..2].CopyTo(tokens);
            originalTokens[2..].CopyTo(tokens[4..]);

            tokens[AckDomainTokenPos] = string.Empty;
            tokens[AckAccHashTokenPos] = string.Empty;
        }
        else
        {
            tokens = originalTokens;

            if (tokens[AckDomainTokenPos] == "_")
            {
                tokens[AckDomainTokenPos] = string.Empty;
            }
        }

        var timestamp = long.Parse(tokens[AckTimestampSeqTokenPos]);
        var offset = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1000000);
        var dateTime = new DateTimeOffset(offset.Ticks, TimeSpan.Zero);

        return new NatsJSMsgMetadata(
            new NatsJSSequencePair(ulong.Parse(tokens[AckStreamSeqTokenPos]), ulong.Parse(tokens[AckConsumerSeqTokenPos])),
            ulong.Parse(tokens[AckNumDeliveredTokenPos]),
            ulong.Parse(tokens[AckNumPendingTokenPos]),
            dateTime,
            tokens[AckStreamTokenPos],
            tokens[AckConsumerTokenPos],
            tokens[AckDomainTokenPos]);
    }
}
