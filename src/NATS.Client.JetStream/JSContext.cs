using System.ComponentModel.DataAnnotations;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class JSContext
{
    private readonly NatsConnection _nats;
    private readonly JSOptions _options;

    public JSContext(NatsConnection nats, JSOptions options)
    {
        _nats = nats;
        _options = options;
    }

    public ValueTask<JSResponse<StreamInfo>> CreateStreamAsync(
        StreamConfiguration request,
        CancellationToken cancellationToken = default) =>
        JSRequestAsync<StreamConfiguration, StreamInfo>(
            subject: $"{_options.Prefix}.STREAM.CREATE.{request.Name}",
            request,
            cancellationToken);

    public ValueTask<JSResponse<StreamMsgDeleteResponse>> DeleteStreamAsync(
        string stream,
        CancellationToken cancellationToken = default) =>
        JSRequestAsync<object, StreamMsgDeleteResponse>(
            subject: $"{_options.Prefix}.STREAM.DELETE.{stream}",
            null,
            cancellationToken);

    public ValueTask<JSResponse<ConsumerInfo>> CreateConsumerAsync(
        ConsumerCreateRequest request,
        CancellationToken cancellationToken = default) =>
        JSRequestAsync<ConsumerCreateRequest, ConsumerInfo>(
            subject: $"{_options.Prefix}.CONSUMER.CREATE.{request.StreamName}.{request.Config.Name}",
            request,
            cancellationToken);

    public async ValueTask<PubAckResponse> PublishAsync<T>(
        string subject,
        T? data,
        NatsPubOpts opts = default,
        CancellationToken cancellationToken = default)
    {
        await using var sub = await _nats.RequestSubAsync<T, PubAckResponse>(
                subject: subject,
                data: data,
                requestOpts: opts,
                replyOpts: default,
                cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                if (msg.Data == null)
                {
                    throw new NatsJetStreamException("No response data received");
                }

                return msg.Data;
            }
        }

        if (sub.EndReason == NatsSubEndReason.Cancelled)
        {
            throw new OperationCanceledException("Publishing inbox subscription was cancelled");
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }

        throw new NatsJetStreamException("No response received");
    }

    public async IAsyncEnumerable<NatsMsg> ConsumeAsync(
        string stream,
        string consumer,
        ConsumerGetnextRequest request,
        NatsSubOpts requestOpts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inbox = $"_INBOX.{Guid.NewGuid():n}";

        await using var sub = await _nats.SubAsync(
            subject: inbox,
            opts: requestOpts,
            builder: NatsSubBuilder.Default,
            cancellationToken);

        await _nats.PubModelAsync(
            subject: $"$JS.API.CONSUMER.MSG.NEXT.{stream}.{consumer}",
            data: request,
            serializer: JsonNatsSerializer.Default,
            replyTo: inbox,
            headers: default,
            cancellationToken);

        await foreach (var msg in sub.Msgs.ReadAllAsync(CancellationToken.None))
        {
            yield return msg;
        }

        if (sub.EndReason == NatsSubEndReason.Cancelled)
        {
            throw new OperationCanceledException("Consumer's inbox subscription was cancelled");
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            throw sub.Exception;
        }
    }

    internal async ValueTask<JSResponse<TResponse>> JSRequestAsync<TRequest, TResponse>(
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

        await using var sub = await _nats.RequestSubAsync<TRequest, TResponse>(
                subject: subject,
                data: request,
                requestOpts: default,
                replyOpts: new NatsSubOpts { Serializer = JSErrorAwareJsonSerializer.Default },
                cancellationToken)
            .ConfigureAwait(false);

        if (await sub.Msgs.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (sub.Msgs.TryRead(out var msg))
            {
                if (msg.Data == null)
                {
                    throw new NatsJetStreamException("No response data received");
                }

                return new JSResponse<TResponse>(msg.Data, default);
            }
        }

        if (sub.EndReason == NatsSubEndReason.Cancelled)
        {
            throw new OperationCanceledException("Request's inbox subscription was cancelled");
        }

        if (sub is { EndReason: NatsSubEndReason.Exception, Exception: not null })
        {
            if (sub.Exception is NatsSubException { InnerException: JSErrorException jsError })
            {
                return new JSResponse<TResponse>(default, jsError.Error);
            }

            throw sub.Exception;
        }

        throw new NatsJetStreamException("No response received");
    }
}

public class NatsJetStreamException : NatsException
{
    public NatsJetStreamException(string message)
        : base(message)
    {
    }

    public NatsJetStreamException(string message, Exception exception)
        : base(message, exception)
    {
    }
}

public record JSOptions
{
    public string Prefix { get; init; } = "$JS.API";
}
