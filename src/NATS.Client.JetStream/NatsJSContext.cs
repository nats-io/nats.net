using System.ComponentModel.DataAnnotations;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>Provides management and access to NATS JetStream streams and consumers.</summary>
public partial class NatsJSContext
{
    /// <inheritdoc cref="NatsJSContext(NATS.Client.Core.NatsConnection,NATS.Client.JetStream.NatsJSOpts)"/>>
    public NatsJSContext(NatsConnection connection)
        : this(connection, new NatsJSOpts(connection.Opts))
    {
    }

    /// <summary>
    /// Creates a NATS JetStream context used to manage and access streams and consumers.
    /// </summary>
    /// <param name="connection">A NATS server connection <see cref="NatsConnection"/> to access the JetStream APIs, publishers and consumers.</param>
    /// <param name="opts">Context wide <see cref="NatsJSOpts"/> JetStream options.</param>
    public NatsJSContext(NatsConnection connection, NatsJSOpts opts)
    {
        Connection = connection;
        Opts = opts;
    }

    internal NatsConnection Connection { get; }

    internal NatsJSOpts Opts { get; }

    /// <summary>
    /// Calls JetStream Account Info API.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The account information based on the NATS connection credentials.</returns>
    public ValueTask<AccountInfoResponse> GetAccountInfoAsync(CancellationToken cancellationToken = default) =>
        JSRequestResponseAsync<object, AccountInfoResponse>(
            subject: $"{Opts.Prefix}.INFO",
            request: null,
            cancellationToken);

    /// <summary>
    /// Sends data to a stream associated with the subject.
    /// </summary>
    /// <param name="subject">Subject to publish the data to.</param>
    /// <param name="data">Data to publish.</param>
    /// <param name="msgId">Sets <c>Nats-Msg-Id</c> header for idempotent message writes.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="opts">Options to be used by publishing command.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the publishing call or the wait for response.</param>
    /// <typeparam name="T">Type of the data being sent.</typeparam>
    /// <returns>
    /// The ACK response to indicate if stream accepted the message as well as additional
    /// information like the sequence number of the message stored by the stream.
    /// </returns>
    /// <exception cref="NatsJSException">There was a problem receiving the response.</exception>
    /// <remarks>
    /// <para>
    /// Note that if the subject isn't backed by a stream or the connected NATS server
    /// isn't running with JetStream enabled, this call will hang waiting for an ACK
    /// until the request times out.
    /// </para>
    /// <para>
    /// By setting <c>msgId</c> you can ensure messages written to a stream only once. JetStream support idempotent
    /// message writes by ignoring duplicate messages as indicated by the Nats-Msg-Id header. If both <c>msgId</c>
    /// and the <c>Nats-Msg-Id</c> header value was set, <c>msgId</c> parameter value will be used.
    /// </para>
    /// </remarks>
    public async ValueTask<PubAckResponse> PublishAsync<T>(
        string subject,
        T? data,
        string? msgId = default,
        NatsHeaders? headers = default,
        NatsPubOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        if (msgId != null)
        {
            headers ??= new NatsHeaders();
            headers["Nats-Msg-Id"] = msgId;
        }

        await using var sub = await Connection.RequestSubAsync<T, PubAckResponse>(
                subject: subject,
                data: data,
                headers: headers,
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

    internal string NewInbox() => $"{Connection.Opts.InboxPrefix}.{Guid.NewGuid():n}";

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
                headers: default,
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
