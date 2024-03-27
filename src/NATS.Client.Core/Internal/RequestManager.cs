using System.Buffers;
using NATS.Client.Core.Commands;

namespace NATS.Client.Core.Internal;

public class RequestManager
{
    private readonly NatsConnection _connection;
    private readonly string _inboxPrefix;

    long _requestId;
    private readonly Dictionary<long, RequestCommand> _requests = new();

    // internal class ReqCmd
    // {
    //     public TaskCompletionSource<NatsMsg<NatsMemoryOwner<byte>>> Tcs;
    // }

    public RequestManager(NatsConnection connection, string inboxPrefix)
    {
        _connection = connection;
        _inboxPrefix = inboxPrefix;
    }

    internal async Task<(RequestCommand, string)> NewRequestAsync(CancellationToken cancellationToken)
    {
        // RequestManager
        await _connection.SubscriptionManager.EnsureMuxInboxSubscribedAsync(cancellationToken).ConfigureAwait(false);
        var req = RequestCommand.Rent(_connection.ObjectPool);
        // req.Tcs = new TaskCompletionSource<NatsMsg<NatsMemoryOwner<byte>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        // var tcs = new TaskCompletionSource<NatsMsg<NatsMemoryOwner<byte>>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var id = Interlocked.Increment(ref _requestId);
        lock (_requests)
        {
            // _requests.Add(id, req);
            _requests.Add(id, req);
        }

        var replyTo = NatsConnection.NewInbox(_inboxPrefix, id); // $"{InboxPrefix}.{id}";

        return (req, replyTo);
    }

    internal void SetRequestReply(string subject, string? replyTo, int sid, in ReadOnlySequence<byte>? headersBuffer, in ReadOnlySequence<byte> payloadBuffer, long id)
    {
        // ReqCmd req;
        RequestCommand tcs;
        lock (_requests)
        {
            // if (!_requests.Remove(id, out req))
            if (!_requests.Remove(id, out tcs))
            {
                return;
            }
        }

        var natsMsg = NatsMsg<NatsMemoryOwner<byte>>.Build(
            subject,
            replyTo,
            headersBuffer,
            payloadBuffer,
            _connection,
            _connection.HeaderParser,
            NatsDefaultSerializer<NatsMemoryOwner<byte>>.Default);

        // req.Tcs.TrySetResult(natsMsg);
        tcs.SetResult(natsMsg);
    }
}
