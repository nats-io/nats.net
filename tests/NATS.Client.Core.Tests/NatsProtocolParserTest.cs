using System.Buffers;

namespace NATS.Client.Core.Tests;

public class NatsProtocolParserTest(ITestOutputHelper output)
{
    [Fact]
    public void T()
    {
        var sequences = new List<ReadOnlySequence<byte>>
        {
            new SequenceBuilder()
                .Append("INFO {\"server_id\":\"nats-server\""u8.ToArray())
                .Append("}\r"u8.ToArray())
                .Append("\nPI"u8.ToArray())
                .ReadOnlySequence,
            new SequenceBuilder()
                .Append("NG"u8.ToArray())
                .Append("\r"u8.ToArray())
                .Append("\n"u8.ToArray())
                .Append("PO"u8.ToArray())
                .ReadOnlySequence,
            new SequenceBuilder()
                .Append("NG\r\n"u8.ToArray())
                .Append("+OK\r\n"u8.ToArray())
                .Append("-ER"u8.ToArray())
                .Append("R 'cra"u8.ToArray())
                .Append("sh!'\r\nPI"u8.ToArray())
                .Append("NG\r\n"u8.ToArray())
                .ReadOnlySequence,
            new SequenceBuilder()
                .Append("MSG subject sid1 reply_to 1\r\nx\r\n"u8.ToArray())
                .ReadOnlySequence,
            new SequenceBuilder()
                .Append("PING\r\n"u8.ToArray())
                .ReadOnlySequence,
        };

        var tokenizer = new NatsProtocolParser.NatsTokenizer();
        var parser = new NatsProtocolParser();

        foreach (var sequence in sequences)
        {
            var buffer = sequence;

            while (parser.TryRead(ref tokenizer, ref buffer))
            {
                output.WriteLine($"Command: {parser.Command}");
                if (parser.Command == NatsProtocolParser.NatsTokenizer.Command.MSG)
                {
                    output.WriteLine($"  subject: {parser.Subject.GetString()}");
                    output.WriteLine($"  sid: {parser.Sid.GetString()}");
                    output.WriteLine($"  reply-to: {parser.ReplyTo.GetString()}");
                    output.WriteLine($"  Payload-Length: {parser.Payload.GetString().Length}");
                    output.WriteLine($"  Payload: {parser.Payload.GetString()}");
                }

                parser.Reset();
            }
        }
    }

    private class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public void SetMemory(ReadOnlyMemory<byte> memory) => Memory = memory;

        public void SetNextSegment(BufferSegment? segment) => Next = segment;

        public void SetRunningIndex(int index) => RunningIndex = index;
    }

    private class SequenceBuilder
    {
        private BufferSegment? _start;
        private BufferSegment? _end;
        private int _length;

        public ReadOnlySequence<byte> ReadOnlySequence => new(_start!, 0, _end!, _end!.Memory.Length);

        // Memory is only allowed rent from ArrayPool.
        public SequenceBuilder Append(ReadOnlyMemory<byte> buffer)
        {
            var segment = new BufferSegment();
            segment.SetMemory(buffer);

            if (_start == null)
            {
                _start = segment;
                _end = segment;
            }
            else
            {
                _end!.SetNextSegment(segment);
                segment.SetRunningIndex(_length);
                _end = segment;
            }

            _length += buffer.Length;

            return this;
        }
    }
}
