using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

var options = NatsOptions.Default with { LoggerFactory = new MinimumConsoleLoggerFactory(LogLevel.Error) };

await using var nats = new NatsConnection(options);

var js = new NatsJSContext(nats);

var stream = await js.CreateStreamAsync("s1", new[] { "s1.*" });
var consumer = await js.CreateConsumerAsync("s1", "c1");
await using var sub = await consumer.CreateSubscription<RawData>(
    controlHandler: async (thisSub, msg) =>
    {
        Console.WriteLine($"____");
        Console.WriteLine($"{msg.Headers?.Code} {msg.Headers?.MessageText}");
        var error = msg.Error?.Error;
        if (error != null)
            Console.WriteLine($"Error: {error}");
    },
    reconnectRequestFactory: () => default,
    heartBeat: TimeSpan.FromSeconds(5),
    opts: new NatsSubOpts { Serializer = new RawDataSerializer() });

await sub.CallMsgNextAsync(new ConsumerGetnextRequest
{
    Batch = 100,
    IdleHeartbeat = TimeSpan.FromSeconds(3).ToNanos(),
    Expires = TimeSpan.FromSeconds(12).ToNanos(),
});

await foreach (var jsMsg in sub.Msgs.ReadAllAsync())
{
    var msg = jsMsg.Msg;
    await jsMsg.AckAsync();
    Console.WriteLine($"____");
    Console.WriteLine($"subject: {msg.Subject}");
    Console.WriteLine($"data: {msg.Data}");
}

class RawData
{
    public RawData(byte[] buffer) => Buffer = buffer;

    public byte[] Buffer { get; }

    public override string ToString() => Encoding.ASCII.GetString(Buffer);
}

class RawDataSerializer : INatsSerializer
{
    public int Serialize<T>(ICountableBufferWriter bufferWriter, T? value)
    {
        if (value is RawData data)
        {
            bufferWriter.Write(data.Buffer);
            return data.Buffer.Length;
        }

        throw new Exception($"Can only work with '{typeof(RawData)}'");
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> buffer) => (T?)Deserialize(buffer, typeof(T));

    public object? Deserialize(in ReadOnlySequence<byte> buffer, Type type)
    {
        if (type != typeof(RawData))
            throw new Exception($"Can only work with '{typeof(RawData)}'");

        return new RawData(buffer.ToArray());
    }
}
