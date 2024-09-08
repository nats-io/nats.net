using System.Net.WebSockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Primitives;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

/// <summary>
/// Options for ClientWebSocketOptions
/// </summary>
public sealed record NatsWebSocketOpts
{
    public static readonly NatsWebSocketOpts Default = new();

    /// <summary>
    /// An optional dictionary of HTTP request headers to be sent with the WebSocket request.
    /// </summary>
    /// <remarks>
    /// Note: this setting will be ignored when running in the Browser, such as when using Blazor WebAssembly,
    /// as the underlying Browser implementation does not support adding headers to a WebSocket.
    /// </remarks>
    public IDictionary<string, StringValues>? RequestHeaders { get; init; }

    /// <summary>
    /// An optional async callback handler for manipulation of ClientWebSocketOptions used for WebSocket connections.
    /// Implementors should use the passed CancellationToken for async operations called by this handler.
    /// </summary>
    public Func<Uri, ClientWebSocketOptions, CancellationToken, ValueTask>? ConfigureClientWebSocketOptions { get; init; } = null;

    internal async ValueTask ApplyClientWebSocketOptionsAsync(
        ClientWebSocketOptions clientWebSocketOptions,
        NatsUri uri,
        NatsTlsOpts tlsOpts,
        CancellationToken cancellationToken)
    {
        if (RequestHeaders != null && !RuntimeInformation.IsOSPlatform(DotnetRuntimeConstants.BrowserPlatform))
        {
            foreach (var entry in RequestHeaders)
            {
                foreach (var value in entry.Value)
                {
                    clientWebSocketOptions.SetRequestHeader(entry.Key, value);
                }
            }
        }

        if (tlsOpts.TryTls(uri))
        {
            var authenticateAsClientOptions = await tlsOpts.AuthenticateAsClientOptionsAsync(uri).ConfigureAwait(false);
            if (authenticateAsClientOptions.ClientCertificates != null)
            {
                clientWebSocketOptions.ClientCertificates = authenticateAsClientOptions.ClientCertificates;
            }

#if !NETSTANDARD2_0
            clientWebSocketOptions.RemoteCertificateValidationCallback = authenticateAsClientOptions.RemoteCertificateValidationCallback;
#endif
        }

        if (ConfigureClientWebSocketOptions != null)
        {
            await ConfigureClientWebSocketOptions(uri.Uri, clientWebSocketOptions, cancellationToken).ConfigureAwait(false);
        }
    }
}
