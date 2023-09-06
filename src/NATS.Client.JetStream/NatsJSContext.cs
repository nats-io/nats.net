using System.ComponentModel.DataAnnotations;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public partial class NatsJSContext
{
    public NatsJSContext(NatsConnection connection)
        : this(connection, new NatsJSOpts(connection.Opts))
    {
    }

    public NatsJSContext(NatsConnection connection, NatsJSOpts opts)
    {
        Connection = connection;
        Opts = opts;
    }

    internal NatsConnection Connection { get; }

    internal NatsJSOpts Opts { get; }

    public ValueTask<AccountInfoResponse> GetAccountInfoAsync(CancellationToken cancellationToken = default) =>
        JSRequestResponseAsync<object, AccountInfoResponse>(
            subject: $"{Opts.Prefix}.INFO",
            request: null,
            cancellationToken);

    public async ValueTask<PubAckResponse> PublishAsync<T>(
        string subject,
        T? data,
        NatsPubOpts opts = default,
        CancellationToken cancellationToken = default)
    {
        await using var sub = await Connection.RequestSubAsync<T, PubAckResponse>(
                subject: subject,
                data: data,
                requestOpts: opts,
                replyOpts: default,
                cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                if (msg.Data == null)
                {
                    throw new NatsJSException("No response data received");
                }

                return msg.Data;
            }
        }

        throw new NatsJSException("No response received");
    }

    internal string NewInbox() => $"{Opts.InboxPrefix}.{Guid.NewGuid():n}";

    internal async ValueTask<TResponse> JSRequestResponseAsync<TRequest, TResponse>(
        string subject,
        TRequest? request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var response = await JSRequestAsync<TRequest, TResponse>(subject, request, cancellationToken);
        response.EnsureSuccess();
        return response.Response!;
    }

    internal async ValueTask<NatsJSResponse<TResponse>> JSRequestAsync<TRequest, TResponse>(
        string subject,
        TRequest? request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        if (request != null)
        {
            Validator.ValidateObject(request, new ValidationContext(request));
        }

        await using var sub = await Connection.RequestSubAsync<TRequest, TResponse>(
                subject: subject,
                data: request,
                requestOpts: default,
                replyOpts: new NatsSubOpts { Serializer = NatsJSErrorAwareJsonSerializer.Default },
                cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                if (msg.Data == null)
                {
                    throw new NatsJSException("No response data received");
                }

                return new NatsJSResponse<TResponse>(msg.Data, default);
            }
        }

        if (sub is NatsSubBase { EndReason: NatsSubEndReason.Exception, Exception: not null } sb)
        {
            if (sb.Exception is NatsSubException { Exception.SourceException: NatsJSApiErrorException jsError })
            {
                // Clear exception here so that subscription disposal won't throw it.
                sb.ClearException();

                return new NatsJSResponse<TResponse>(default, jsError.Error);
            }

            throw sb.Exception;
        }

        throw new NatsJSException("No response received");
    }
}

public class NatsJSDuplicateMessageException : NatsJSException
{
    public NatsJSDuplicateMessageException(long sequence)
        : base($"Duplicate of {sequence}") =>
        Sequence = sequence;

    public long Sequence { get; }
}
