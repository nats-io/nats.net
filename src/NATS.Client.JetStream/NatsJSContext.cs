using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>Provides management and access to NATS JetStream streams and consumers.</summary>
public partial class NatsJSContext
{
    private readonly ILogger _logger;

    /// <inheritdoc cref="NatsJSContext(NATS.Client.Core.INatsConnection,NATS.Client.JetStream.NatsJSOpts)"/>>
    public NatsJSContext(INatsConnection connection)
        : this(connection, new NatsJSOpts(connection.Opts))
    {
    }

    /// <summary>
    /// Creates a NATS JetStream context used to manage and access streams and consumers.
    /// </summary>
    /// <param name="connection">A NATS server connection <see cref="NatsConnection"/> to access the JetStream APIs, publishers and consumers.</param>
    /// <param name="opts">Context wide <see cref="NatsJSOpts"/> JetStream options.</param>
    public NatsJSContext(INatsConnection connection, NatsJSOpts opts)
    {
        Connection = connection;
        Opts = opts;
        _logger = connection.Opts.LoggerFactory.CreateLogger<NatsJSContext>();
    }

    public INatsConnection Connection { get; }

    /// <inheritdoc />
    public NatsJSOpts Opts { get; }

    /// <summary>
    /// Calls JetStream Account Info API.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>The account information based on the NATS connection credentials.</returns>
    public ValueTask<AccountInfoResponse> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        var props = new NatsPublishProps(
            "{prefix}.{entity}",
            new Dictionary<string, object>()
            {
                { "prefix", Opts.Prefix },
                { "entity", "INFO" },
            });
        return JSRequestResponseAsync<object, AccountInfoResponse>(
            props: props,
            request: null,
            cancellationToken);
    }

    /// <summary>
    /// Sends data to a stream associated with the subject.
    /// </summary>
    /// <param name="subject">Subject to publish the data to.</param>
    /// <param name="data">Data to publish.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
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
        INatsSerialize<T>? serializer = default,
        NatsJSPubOpts? opts = default,
        NatsHeaders? headers = default,
        CancellationToken cancellationToken = default)
    {
        var result = await TryPublishAsync(subject, data, serializer, opts, headers, cancellationToken);
        if (!result.Success)
        {
            throw result.Error;
        }

        return result.Value;
    }

    /// <summary>
    /// Sends data to a stream associated with the subject.
    /// </summary>
    /// <param name="subject">Subject to publish the data to.</param>
    /// <param name="data">Data to publish.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
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
    /// Use this method to avoid exceptions.
    /// </para>
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
    public async ValueTask<NatsResult<PubAckResponse>> TryPublishAsync<T>(
        string subject,
        T? data,
        INatsSerialize<T>? serializer = default,
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
            if (Connection.Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
            {
                var noReply = false;
                NatsMsg<PubAckResponse> msg;
                try
                {
                    msg = await Connection.RequestAsync<T, PubAckResponse>(
                        subject: subject,
                        data: data,
                        headers: headers,
                        requestSerializer: serializer,
                        replySerializer: NatsJSJsonSerializer<PubAckResponse>.Default,
                        requestOpts: opts,
                        replyOpts: new NatsSubOpts { Timeout = Connection.Opts.RequestTimeout },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (NatsNoReplyException)
                {
                    noReply = true;
                    msg = default;
                }
                catch (Exception ex)
                {
                    return ex;
                }

                if (noReply || msg.HasNoResponders)
                {
                    _logger.LogDebug(NatsJSLogEvents.PublishNoResponseRetry, "No response received, retrying {RetryCount}/{RetryMax}", i + 1, retryMax);
                    await Task.Delay(retryWait, cancellationToken);
                    continue;
                }

                if (msg.Data == null)
                {
                    return new NatsJSException("No response data received");
                }

                return msg.Data;
            }

            await using var sub = await Connection.CreateRequestSubAsync<T, PubAckResponse>(
                    subject: subject,
                    data: data,
                    headers: headers,
                    requestSerializer: serializer,
                    replySerializer: NatsJSJsonSerializer<PubAckResponse>.Default,
                    requestOpts: opts,
                    replyOpts: new NatsSubOpts
                    {
                        // It's important to set the timeout here so that the subscription can be
                        // stopped if the server doesn't respond or more likely case is that if there
                        // is a reconnect to the cluster between the request and waiting for a response,
                        // without the timeout the publish call will hang forever since the server
                        // which received the request won't be there to respond anymore.
                        Timeout = Connection.Opts.RequestTimeout,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // If JetStream is disabled, a no responders error will be returned.
                // No responders error might also happen when reconnecting to cluster.
                // We should retry in those cases.
                if (msg.HasNoResponders)
                {
                    break;
                }
                else if (msg.Data == null)
                {
                    return new NatsJSException("No response data received");
                }

                return msg.Data;
            }

            if (i < retryMax)
            {
                _logger.LogDebug(NatsJSLogEvents.PublishNoResponseRetry, "No response received, retrying {RetryCount}/{RetryMax}", i + 1, retryMax);
                await Task.Delay(retryWait, cancellationToken);
            }
        }

        // We throw a specific exception here for convenience so that the caller doesn't
        // have to check for the exception message etc.
        return new NatsJSPublishNoResponseException();
    }

    public async ValueTask<NatsJSPublishConcurrentFuture> PublishConcurrentAsync<T>(
        string subject,
        T? data,
        INatsSerialize<T>? serializer = default,
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

        var sub = await Connection.CreateRequestSubAsync<T, PubAckResponse>(
                    subject: subject,
                    data: data,
                    headers: headers,
                    requestSerializer: serializer,
                    replySerializer: NatsJSJsonSerializer<PubAckResponse>.Default,
                    requestOpts: opts,
                    replyOpts: new NatsSubOpts
                    {
                        // It's important to set the timeout here so that the subscription can be
                        // stopped if the server doesn't respond or more likely case is that if there
                        // is a reconnect to the cluster between the request and waiting for a response,
                        // without the timeout the publish call will hang forever since the server
                        // which received the request won't be there to respond anymore.
                        Timeout = Connection.Opts.RequestTimeout,

                        // If JetStream is disabled, a no responders error will be returned
                        // No responders error might also happen when reconnecting to cluster
                        ThrowIfNoResponders = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        return new NatsJSPublishConcurrentFuture(sub);
    }

    /// <inheritdoc />
    public string NewBaseInbox() => NatsConnection.NewInbox(Connection.Opts.InboxPrefix);

    /// <inheritdoc />
    public async ValueTask<TResponse> JSRequestResponseAsync<TRequest, TResponse>(
        string subject,
        TRequest? request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var response = await JSRequestAsync<TRequest, TResponse>(new NatsPublishProps(subject), request, cancellationToken);
        response.EnsureSuccess();
        return response.Response!;
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> JSRequestResponseAsync<TRequest, TResponse>(
        NatsPublishProps props,
        TRequest? request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var response = await JSRequestAsync<TRequest, TResponse>(props, request, cancellationToken);
        response.EnsureSuccess();
        return response.Response!;
    }

    internal static void ThrowIfInvalidStreamName([NotNull] string? name, [CallerArgumentExpression("name")] string? paramName = null)
    {
#if NETSTANDARD
        ArgumentNullExceptionEx.ThrowIfNull(name, paramName);
#else
        ArgumentNullException.ThrowIfNull(name, paramName);
#endif

        if (name.Length == 0)
        {
            ThrowEmptyException(paramName);
        }

#if NETSTANDARD2_0
        if (name.Contains(".") || name.Contains(" "))
#else
        var nameSpan = name.AsSpan();
        if (nameSpan.IndexOfAny(" .") >= 0)
#endif
        {
            ThrowInvalidStreamNameException(paramName);
        }
    }

    internal async ValueTask<NatsJSResponse<TResponse>> JSRequestAsync<TRequest, TResponse>(
        NatsPublishProps props,
        TRequest? request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var result = await TryJSRequestAsync<TRequest, TResponse>(props, request, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw result.Error;
        }

        return result.Value;
    }

    internal async ValueTask<NatsResult<NatsJSResponse<TResponse>>> TryJSRequestAsync<TRequest, TResponse>(
        NatsPublishProps props,
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

        if (Connection.Opts.RequestReplyMode == NatsRequestReplyMode.Direct)
        {
            NatsMsg<NatsJSApiResult<TResponse>> msg;
            try
            {
                msg = await Connection.RequestAsync<TRequest, NatsJSApiResult<TResponse>>(
                    subject: string.Empty,
                    data: request,
                    headers: null,
                    requestOpts: new NatsPubOpts { Props = props },
                    replyOpts: new NatsSubOpts { Timeout = Connection.Opts.RequestTimeout },
                    requestSerializer: NatsJSJsonSerializer<TRequest>.Default,
                    replySerializer: NatsJSJsonDocumentSerializer<TResponse>.Default,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (NatsNoReplyException)
            {
                return new NatsJSApiNoResponseException();
            }
            catch (NatsException e)
            {
                return e;
            }

            if (msg.HasNoResponders)
            {
                return new NatsNoRespondersException();
            }

            if (msg.Error is { } messageError)
            {
                return messageError;
            }

            if (msg.Data.HasException)
            {
                return msg.Data.Exception;
            }

            if (msg.Data.HasError)
            {
                return new NatsJSResponse<TResponse>(null, msg.Data.Error);
            }

            return new NatsJSResponse<TResponse>(msg.Data.Value, null);
        }

        await using var sub = await Connection.CreateRequestSubAsync<TRequest, NatsJSApiResult<TResponse>>(
                subject: string.Empty,
                data: request,
                headers: default,
                requestOpts: new NatsPubOpts { Props = props },
                replyOpts: new NatsSubOpts { Timeout = Connection.Opts.RequestTimeout },
                requestSerializer: NatsJSJsonSerializer<TRequest>.Default,
                replySerializer: NatsJSJsonDocumentSerializer<TResponse>.Default,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await foreach (var msg in sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (msg.HasNoResponders)
            {
                return new NatsNoRespondersException();
            }

            if (msg.Error is { } messageError)
            {
                return messageError;
            }

            if (msg.Data.HasException)
            {
                return msg.Data.Exception;
            }

            if (msg.Data.HasError)
            {
                return new NatsJSResponse<TResponse>(null, msg.Data.Error);
            }

            return new NatsJSResponse<TResponse>(msg.Data.Value, null);
        }

        if (sub is NatsSubBase { EndReason: NatsSubEndReason.Exception, Exception: not null } sb)
        {
            return sb.Exception;
        }

        if (sub.EndReason != NatsSubEndReason.None)
        {
            return new NatsJSApiNoResponseException(sub.EndReason);
        }

        return new NatsJSApiNoResponseException();
    }

    private static void ConvertDomain(StreamSource streamSource)
    {
        if (string.IsNullOrEmpty(streamSource.Domain))
        {
            return;
        }

        if (streamSource.External != null)
        {
            throw new ArgumentException("Both domain and external are set");
        }

        streamSource.External = new ExternalStreamSource { Api = $"$JS.{streamSource.Domain}.API" };
    }

    [DoesNotReturn]
    private static void ThrowInvalidStreamNameException(string? paramName) =>
        throw new ArgumentException("Stream name cannot contain ' ', '.'", paramName);

    [DoesNotReturn]
    private static void ThrowEmptyException(string? paramName) =>
        throw new ArgumentException("The value cannot be an empty string.", paramName);
}
