using System.Buffers;
using BenchmarkDotNet.Attributes;
using NATS.Client.Core;

namespace MicroBenchmark;

[ShortRunJob]
[MemoryDiagnoser]
[PlainExporter]
public class NatsProtoParserBench
{
    private List<ReadOnlySequence<byte>> _sequences;
    private NatsProtocolParser _parser;

    [GlobalSetup]
    public void Setup()
    {
        _sequences =
        [
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
                .ReadOnlySequence
        ];

        _parser = new NatsProtocolParser();
    }

    [Benchmark]
    public int Parse()
    {
        var tokenizer = new NatsProtocolParser.NatsTokenizer();
        var count = 0;

        foreach (var sequence in _sequences)
        {
            var buffer = sequence;

            while (_parser.TryRead(ref tokenizer, ref buffer))
            {
                switch (_parser.Command)
                {
                case NatsProtocolParser.NatsTokenizer.Command.INFO:
                case NatsProtocolParser.NatsTokenizer.Command.PING:
                case NatsProtocolParser.NatsTokenizer.Command.PONG:
                case NatsProtocolParser.NatsTokenizer.Command.OK:
                case NatsProtocolParser.NatsTokenizer.Command.ERR:
                case NatsProtocolParser.NatsTokenizer.Command.MSG:
                    count++;
                    break;
                }

                _parser.Reset();
            }
        }

        if (count != 8)
            throw new Exception("Invalid count");

        return count;
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
