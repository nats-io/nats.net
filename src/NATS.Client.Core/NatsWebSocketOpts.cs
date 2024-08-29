using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Primitives;

namespace NATS.Client.Core;

/// <summary>
/// Options for ClientWebSocketOptions
/// </summary>
public sealed record NatsWebSocketOpts
{
    public static readonly NatsWebSocketOpts Default = new();

    /// <summary>
    /// An optional, HTTP headers adding to clientWebSocketOptions
    /// </summary>
    /// <remarks>
    /// Note: Setting HTTP header values is not supported by Blazor WebAssembly as the underlying browser implementation does not support adding headers to a WebSocket.
    /// </remarks>
    public Dictionary<string, StringValues>? RequestHeaders { get; init; }

    /// <summary>
    /// An optional, async callback handler for manipulation of ClientWebSocketOptions used for WebSocket connections.
    /// Implementors should use the passed CancellationToken for async operations called by this handler.
    /// </summary>
    public Func<Uri, ClientWebSocketOptions, CancellationToken, X509CertificateCollection?, RemoteCertificateValidationCallback?, ValueTask>? ConfigureWebSocketOpts { get; init; } = null;
}
