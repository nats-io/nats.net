using System.Buffers;
using System.Text;
using Synadia.Orbit.Testing.NatsServerProcessManager;

namespace NATS.Client.Core.Tests;

public class LowLevelApiTest
{
    private readonly ITestOutputHelper _output;

    public LowLevelApiTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Sub_custom_builder_test()
    {
        await using var server = await NatsServerProcess.StartAsync();
        await using var nats = new NatsConnection(new NatsOpts { Url = server.Url });

        var subject = "foo.*";
        var builder = new NatsSubCustomTestBuilder(_output);
        var sub = builder.Build(subject, default, nats, nats.SubscriptionManager);
        await nats.AddSubAsync(sub);

        await Retry.Until(
            "subscription is ready",
            () => builder.IsSynced,
            async () => await nats.PublishAsync("foo.sync"));

        for (var i = 0; i < 10; i++)
        {
            var headers = new NatsHeaders { { "X-Test", $"value-{i}" } };
            await nats.PublishAsync($"foo.data{i}", i, headers, "bar", NatsDefaultSerializer<int>.Default);
        }

        await nats.PublishAsync("foo.done");
        await builder.Done;

        Assert.Equal(10, builder.Messages.Count());

        await sub.DisposeAsync();
    }

    private class NatsSubTest : NatsSubBase
    {
        private readonly NatsSubCustomTestBuilder _builder;
        private readonly ITestOutputHelper _output;

        public NatsSubTest(string subject, NatsConnection connection, NatsSubCustomTestBuilder builder, ITestOutputHelper output, INatsSubscriptionManager manager)
        : base(connection, manager, subject, default, default)
        {
            _builder = builder;
            _output = output;
        }

        protected override ValueTask ReceiveInternalAsync(string subject, string? replyTo, ReadOnlySequence<byte>? headersBuffer, ReadOnlySequence<byte> payloadBuffer)
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

            return default;
        }

        protected override void TryComplete()
        {
        }
    }

    private class NatsSubCustomTestBuilder
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

        public NatsSubTest Build(string subject, NatsSubOpts? opts, NatsConnection connection, INatsSubscriptionManager manager)
        {
            return new NatsSubTest(subject, connection, builder: this, _output, manager);
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
