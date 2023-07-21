using System.Buffers;
using System.Text;

namespace NATS.Client.Core.Tests;

public class LowLevelApiTest
{
    private readonly ITestOutputHelper _output;

    public LowLevelApiTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Sub_custom_builder_test()
    {
        await using var server = new NatsServer();
        var nats = server.CreateClientConnection();

        var builder = new NatsSubCustomTestBuilder(_output);
        var sub = await nats.SubAsync<NatsSubTest>("foo.*", opts: default, builder);

        await Retry.Until(
            "subscription is ready",
            () => builder.IsSynced,
            async () => await nats.PubAsync("foo.sync"));

        for (var i = 0; i < 10; i++)
        {
            var headers = new NatsHeaders { { "X-Test", $"value-{i}" } };
            await nats.PubModelAsync<int>($"foo.data{i}", i, JsonNatsSerializer.Default, "bar", headers);
        }

        await nats.PubAsync("foo.done");
        await builder.Done;

        Assert.Equal(10, builder.Messages.Count());

        await sub.DisposeAsync();
    }

    private class NatsSubTest : INatsSub
    {
        private readonly NatsSubCustomTestBuilder _builder;
        private readonly ITestOutputHelper _output;
        private readonly ISubscriptionManager _manager;

        public NatsSubTest(NatsSubCustomTestBuilder builder, ITestOutputHelper output, ISubscriptionManager manager)
        {
            _builder = builder;
            _output = output;
            _manager = manager;
        }

        public string Subject => string.Empty;

        public string QueueGroup => string.Empty;

        public int? PendingMsgs => 0;

        public int Sid => 0;

        public void Ready()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask ReceiveAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
        {
            if (subject.EndsWith(".sync"))
            {
                _builder.Sync();
            }
            else if (subject.EndsWith(".done"))
            {
                _builder.MarkAsDone();
            }
            else
            {
                var headers = headersBuffer?.ToArray();
                var payload = payloadBuffer.ToArray();

                var sb = new StringBuilder();
                sb.AppendLine($"Subject: {subject}");
                sb.AppendLine($"Reply-To: {replyTo}");
                sb.Append($"Headers: ");
                if (headers != null)
                    sb.Append(Encoding.ASCII.GetString(headers).Replace("\r\n", " "));
                sb.AppendLine();
                sb.AppendLine($"Payload: {Encoding.ASCII.GetString(payload)}");

                _output.WriteLine(sb.ToString());

                _builder.MessageReceived(sb.ToString());
            }

            return ValueTask.CompletedTask;
        }
    }

    private class NatsSubCustomTestBuilder : INatsSubBuilder<NatsSubTest>
    {
        private readonly ITestOutputHelper _output;
        private readonly WaitSignal _done = new();
        private readonly List<string> _messages = new();
        private int _sync;

        public NatsSubCustomTestBuilder(ITestOutputHelper output) => _output = output;

        public bool IsSynced => Volatile.Read(ref _sync) == 1;

        public WaitSignal Done => _done;

        public IEnumerable<string> Messages
        {
            get
            {
                lock (_messages)
                    return _messages.ToArray();
            }
        }

        public NatsSubTest Build(string subject, NatsSubOpts? opts, NatsConnection connection, ISubscriptionManager manager)
        {
            return new NatsSubTest(builder: this, _output, manager);
        }

        public void Sync() => Interlocked.Exchange(ref _sync, 1);

        public void MarkAsDone() => _done.Pulse();

        public void MessageReceived(string message)
        {
            lock (_messages)
                _messages.Add(message);
        }
    }
}
