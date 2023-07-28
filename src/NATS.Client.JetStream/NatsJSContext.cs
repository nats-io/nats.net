using System.ComponentModel.DataAnnotations;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSContext
{
    public NatsJSContext(NatsConnection nats)
        : this(nats, new NatsJSOptions())
    {
    }

    public NatsJSContext(NatsConnection nats, NatsJSOptions options)
    {
        Nats = nats;
        Options = options;
    }

    internal NatsConnection Nats { get; }

    internal NatsJSOptions Options { get; }

    public ValueTask<AccountInfoResponse> GetAccountInfoAsync(CancellationToken cancellationToken = default) =>
        JSRequestResponseAsync<object, AccountInfoResponse>(
            subject: $"{Options.Prefix}.INFO",
            request: null,
            cancellationToken);

    public async ValueTask<PubAckResponse> PublishAsync<T>(
        string subject,
        T? data,
        NatsPubOpts opts = default,
        CancellationToken cancellationToken = default)
    {
        await using var sub = await Nats.RequestSubAsync<T, PubAckResponse>(
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

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }

        throw new NatsJSException("No response received");
    }

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

        await using var sub = await Nats.RequestSubAsync<TRequest, TResponse>(
                subject: subject,
                data: request,
                requestOpts: default,
                replyOpts: new NatsSubOpts { Serializer = JSErrorAwareJsonSerializer.Default },
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

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            if (sub.Exception is NatsSubException { InnerException: JSErrorException jsError })
            {
                return new NatsJSResponse<TResponse>(default, jsError.Error);
            }

            throw sub.Exception;
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
