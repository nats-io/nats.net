using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>Provides management and access to NATS JetStream streams and consumers.</summary>
public partial class NatsJSContext
{
    private readonly ILogger _logger;

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
        _logger = connection.Opts.LoggerFactory.CreateLogger<NatsJSContext>();
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
    /// <param name="opts">Publish options.</param>
    /// <param name="headers">Optional message headers.</param>
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
        NatsJSPubOpts? opts = default,
        NatsHeaders? headers = default,
        CancellationToken cancellationToken = default)
    {
        if (opts != null)
        {
            if (opts.MsgId != null)
            {
                headers ??= new NatsHeaders();
                headers["Nats-Msg-Id"] = opts.MsgId;
            }

            if (opts.ExpectedLastMsgId != null)
            {
                headers ??= new NatsHeaders();
                headers["Nats-Expected-Last-Msg-Id"] = opts.ExpectedLastMsgId;
            }

            if (opts.ExpectedStream != null)
            {
                headers ??= new NatsHeaders();
                headers["Nats-Expected-Stream"] = opts.ExpectedStream;
            }

            if (opts.ExpectedLastSequence != null)
            {
                headers ??= new NatsHeaders();
                headers["Nats-Expected-Last-Sequence"] = opts.ExpectedLastSequence.ToString();
            }

            if (opts.ExpectedLastSubjectSequence != null)
            {
                headers ??= new NatsHeaders();
                headers["Nats-Expected-Last-Subject-Sequence"] = opts.ExpectedLastSubjectSequence.ToString();
            }
        }

        opts ??= NatsJSPubOpts.Default;
        var retryMax = opts.RetryAttempts;
        var retryWait = opts.RetryWaitBetweenAttempts;

        for (var i = 0; i < retryMax; i++)
        {
            await using var sub = await Connection.RequestSubAsync<T, PubAckResponse>(
                    subject: subject,
                    data: data,
                    headers: headers,
                    requestOpts: opts,
                    replyOpts: new NatsSubOpts
                    {
                        Serializer = NatsJSJsonSerializer.Default,

                        // It's important to set the timeout here so that the subscription can be
                        // stopped if the server doesn't respond or more likely case is that if there
                        // is a reconnect to the cluster between the request and waiting for a response,
                        // without the timeout the publish call will hang forever since the server
                        // which received the request won't be there to respond anymore.
                        Timeout = Connection.Opts.RequestTimeout,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            while (await sub.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (sub.Msgs.TryRead(out var msg))
                {
                    if (msg.Data == null)
                    {
                        throw new NatsJSException("No response data received");
                    }

                    return msg.Data;
                }
            }

            if (i < retryMax)
            {
                _logger.LogDebug(NatsJSLogEvents.PublishNoResponseRetry, "No response received, retrying {RetryCount}/{RetryMax}", i + 1, retryMax);
                await Task.Delay(retryWait, cancellationToken);
            }
        }

        // We throw a specific exception here for convenience so that the caller doesn't
        // have to check for the exception message etc.
        throw new NatsJSPublishNoResponseException();
    }

    public ValueTask<PubAckResponse> PublishAsync(
        string subject,
        NatsJSPubOpts? opts = default,
        NatsHeaders? headers = default,
        CancellationToken cancellationToken = default) =>
        PublishAsync<object?>(subject, default, opts, headers, cancellationToken);

    internal string NewInbox() => NatsConnection.NewInbox(Connection.Opts.InboxPrefix);

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
            // TODO: Can't validate using JSON serializer context at the moment.
            // Validator.ValidateObject(request, new ValidationContext(request));
        }

        var cancellationTimer = Connection.GetCancellationTimer(cancellationToken);
        try
        {
            await using var sub = await Connection.RequestSubAsync<TRequest, TResponse>(
                    subject: subject,
                    data: request,
                    headers: default,
                    requestOpts: new NatsPubOpts { Serializer = NatsJSJsonSerializer.Default },
                    replyOpts: new NatsSubOpts { Serializer = NatsJSErrorAwareJsonSerializer.Default },
                    cancellationTimer.Token)
                .ConfigureAwait(false);

            if (await sub.Msgs.WaitToReadAsync(cancellationTimer.Token).ConfigureAwait(false))
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
        finally
        {
            cancellationTimer.TryReturn();
        }
    }
}
