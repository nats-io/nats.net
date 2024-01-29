using System.Diagnostics;
using NATS.Client.Core;

namespace NATS.Client.Services;

/// <summary>
/// NATS service exception.
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly struct NatsSvcMsg<T>
{
    private readonly NatsMsg<T> _msg;
    private readonly NatsSvcEndpointBase? _endPoint;

    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcMsg{T}"/>.
    /// </summary>
    /// <param name="msg">NATS message.</param>
    /// <param name="endPoint">Service endpoint.</param>
    /// <param name="exception">Optional exception if there were any errors.</param>
    public NatsSvcMsg(NatsMsg<T> msg, NatsSvcEndpointBase? endPoint, Exception? exception)
    {
        Exception = exception;
        _msg = msg;
        _endPoint = endPoint;
    }

    /// <summary>
    /// Activity used to trace the receiving of the this message. It can be used to create child activities under this context.
    /// </summary>
    /// <seealso cref="NatsSvcMsgTelemetryExtensions.StartChildActivity{T}"/>
    public Activity? Activity => _msg.Activity;

    /// <summary>
    /// Optional exception if there were any errors.
    /// </summary>
    /// <remarks>
    /// Check this property to see if there were any errors before processing the message.
    /// </remarks>
    public Exception? Exception { get; }

    /// <summary>
    /// Message subject.
    /// </summary>
    public string Subject => _msg.Subject;

    /// <summary>
    /// Message data.
    /// </summary>
    public T? Data => _msg.Data;

    /// <summary>
    /// Message reply-to subject.
    /// </summary>
    public string? ReplyTo => _msg.ReplyTo;

    /// <summary>
    /// Send a reply with an empty message body.
    /// </summary>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">Optional publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    public ValueTask ReplyAsync(NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(headers, replyTo, opts, cancellationToken);

    /// <summary>
    /// Send a reply with a message body.
    /// </summary>
    /// <param name="data">Data to be sent.</param>
    /// <param name="headers">Optional message headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Optional publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="TReply">A serializable type as data.</typeparam>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    public ValueTask ReplyAsync<TReply>(TReply data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<TReply>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _msg.ReplyAsync(data, headers, replyTo, serializer, opts, cancellationToken);

    /// <summary>
    /// Reply with an error and additional data as error body.
    /// </summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error message.</param>
    /// <param name="data">Error body.</param>
    /// <param name="headers">Optional additional headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Optional publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <typeparam name="TReply">A serializable type as data.</typeparam>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    public ValueTask ReplyErrorAsync<TReply>(int code, string message, TReply data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<TReply>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        headers ??= new NatsHeaders();
        headers.Add("Nats-Service-Error-Code", $"{code}");
        headers.Add("Nats-Service-Error", $"{message}");

        _endPoint?.IncrementErrors();
        _endPoint?.SetLastError($"{message} ({code})");

        return ReplyAsync(data, headers, replyTo, serializer, opts, cancellationToken);
    }

    /// <summary>
    /// Reply with an error.
    /// </summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error message.</param>
    /// <param name="headers">Optional additional headers.</param>
    /// <param name="replyTo">Optional reply-to subject.</param>
    /// <param name="opts">Optional publishing options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    public ValueTask ReplyErrorAsync(int code, string message, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default)
    {
        headers ??= new NatsHeaders();
        headers.Add("Nats-Service-Error", $"{message}");
        headers.Add("Nats-Service-Error-Code", $"{code}");

        _endPoint?.IncrementErrors();
        _endPoint?.SetLastError($"{code}:{message}");

        return ReplyAsync(headers: headers, replyTo: replyTo, opts: opts, cancellationToken: cancellationToken);
    }
}
