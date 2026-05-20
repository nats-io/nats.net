using NATS.Client.Core;
using NATS.Client.Services;

// ReSharper disable once CheckNamespace
namespace NATS.Net;

/// <summary>
/// Extension helpers for inspecting service responses on the requester side.
/// </summary>
/// <remarks>
/// Service handlers signal errors using the <c>Nats-Service-Error</c> and
/// <c>Nats-Service-Error-Code</c> response headers (see
/// <see cref="NatsSvcMsg{T}.ReplyErrorAsync(int,string,NatsHeaders?,string?,NatsPubOpts?,CancellationToken)"/>).
/// These helpers surface that convention on the request side.
/// </remarks>
public static class NatsSvcMsgExtensions
{
    /// <summary>
    /// Returns <c>true</c> when the response does not carry a <c>Nats-Service-Error</c> header.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="msg">The response message to inspect.</param>
    /// <returns><c>true</c> if the response is a service success; otherwise <c>false</c>.</returns>
    public static bool IsServiceSuccess<T>(this NatsMsg<T> msg)
        => msg.Headers is null || !msg.Headers.ContainsKey(NatsSvcConstants.ServiceErrorHeader);

    /// <summary>
    /// Returns <c>true</c> when the response does not carry a <c>Nats-Service-Error</c> header.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="msg">The response message to inspect.</param>
    /// <param name="throwOnNoResponders">When <c>true</c>, throws <see cref="NatsNoRespondersException"/> first if the response is a no-responders sentinel.</param>
    /// <returns><c>true</c> if the response is a service success; otherwise <c>false</c>.</returns>
    /// <exception cref="NatsNoRespondersException">Thrown when <paramref name="throwOnNoResponders"/> is <c>true</c> and no service responded.</exception>
    public static bool IsServiceSuccess<T>(this NatsMsg<T> msg, bool throwOnNoResponders)
    {
        if (throwOnNoResponders && msg.HasNoResponders)
        {
            throw new NatsNoRespondersException();
        }

        return msg.IsServiceSuccess();
    }

    /// <summary>
    /// Throws <see cref="NatsSvcEndpointException"/> when the response carries a <c>Nats-Service-Error</c> header.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="msg">The response message to inspect.</param>
    /// <param name="throwOnNoResponders">When <c>true</c>, throws <see cref="NatsNoRespondersException"/> first if the response is a no-responders sentinel.</param>
    /// <returns>The same message, to allow fluent chaining.</returns>
    /// <exception cref="NatsSvcEndpointException">Thrown when the response carries a service error.</exception>
    /// <exception cref="NatsNoRespondersException">Thrown when <paramref name="throwOnNoResponders"/> is <c>true</c> and no service responded.</exception>
    public static NatsMsg<T> EnsureServiceSuccess<T>(this NatsMsg<T> msg, bool throwOnNoResponders = false)
    {
        var status = msg.GetServiceStatus();

        if (throwOnNoResponders && status.HasNoResponders)
        {
            throw new NatsNoRespondersException();
        }

        if (status.Message is not null)
        {
            throw new NatsSvcEndpointException(status.Code, status.Message);
        }

        return msg;
    }

    /// <summary>
    /// Reads the service status from the response, combining the <c>Nats-Service-Error</c> /
    /// <c>Nats-Service-Error-Code</c> headers with the no-responders sentinel.
    /// </summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="msg">The response message to inspect.</param>
    /// <returns>The parsed <see cref="NatsSvcStatus"/>.</returns>
    public static NatsSvcStatus GetServiceStatus<T>(this NatsMsg<T> msg)
    {
        if (msg.HasNoResponders)
        {
            return NatsSvcStatus.NoResponders;
        }

        var headers = msg.Headers;
        if (headers is null || !headers.TryGetValue(NatsSvcConstants.ServiceErrorHeader, out var errValue))
        {
            return NatsSvcStatus.Success;
        }

        var code = 0;
        if (headers.TryGetValue(NatsSvcConstants.ServiceErrorCodeHeader, out var codeValue)
            && int.TryParse(codeValue.ToString(), out var parsed))
        {
            code = parsed;
        }

        return NatsSvcStatus.FromError(code, errValue.ToString());
    }
}
