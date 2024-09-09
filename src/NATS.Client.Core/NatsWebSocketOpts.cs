using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
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
    /// Not supported when running in the Browser, such as when using Blazor WebAssembly,
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
        if (RequestHeaders != null)
        {
            foreach (var entry in RequestHeaders)
            {
                // SetRequestHeader overwrites if called multiple times;
                // RFC7230 Section 3.2.2 allows for combining them with a comma
                // https://www.rfc-editor.org/rfc/rfc7230#section-3.2.2
                clientWebSocketOptions.SetRequestHeader(entry.Key, string.Join(",", entry.Value));
            }
        }

        if (tlsOpts.HasTlsCerts)
        {
            var authenticateAsClientOptions = await tlsOpts.AuthenticateAsClientOptionsAsync(uri).ConfigureAwait(false);
            var collection = new X509CertificateCollection();

            // must match LoadClientCertFromX509 method in SslClientAuthenticationOptions.cs
#if NET8_0_OR_GREATER
            if (authenticateAsClientOptions.ClientCertificateContext != null)
            {
                collection.Add(authenticateAsClientOptions.ClientCertificateContext.TargetCertificate);
            }
#else
            if (authenticateAsClientOptions.ClientCertificates != null)
            {
                collection.AddRange(authenticateAsClientOptions.ClientCertificates);
            }
#endif
            if (collection.Count > 0)
            {
                clientWebSocketOptions.ClientCertificates = collection;
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
