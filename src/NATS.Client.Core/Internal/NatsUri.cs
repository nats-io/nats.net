namespace NATS.Client.Core.Internal;

internal sealed record NatsUri
{
    public const string DefaultScheme = "nats";

    public NatsUri(string urlString, bool isSeed, string defaultScheme = DefaultScheme)
    {
        IsSeed = isSeed;
        if (!urlString.Contains("://"))
        {
            urlString = $"{defaultScheme}://{urlString}";
        }

        var uriBuilder = new UriBuilder(new Uri(urlString, UriKind.Absolute));
        if (string.IsNullOrEmpty(uriBuilder.Host))
        {
            uriBuilder.Host = "localhost";
        }

        switch (uriBuilder.Scheme)
        {
        case "tls":
            IsTls = true;
            goto case "nats";
        case "nats":
            if (uriBuilder.Port == -1)
                uriBuilder.Port = 4222;
            break;
        case "ws":
            IsWebSocket = true;
            break;
        case "wss":
            IsWebSocket = true;
            break;
        default:
            throw new ArgumentException($"unsupported scheme {uriBuilder.Scheme} in nats URL {urlString}", urlString);
        }

        Uri = uriBuilder.Uri;
    }

    public Uri Uri { get; init; }

    public bool IsSeed { get; }

    public bool IsTls { get; }

    public bool IsWebSocket { get; }

    public string Host => Uri.Host;

    public int Port => Uri.Port;

    /// <summary>
    /// URI of a server learned from INFO <c>connect_urls</c>. An entry is <c>host[:port]</c> and
    /// nothing else: the scheme, which picks the transport and the TLS mode, is the client's own.
    /// </summary>
    /// <remarks>
    /// The entry is checked against that grammar before any URL parsing. A URL parser would read
    /// more into it: in <c>a:1@evil:2</c> the first part becomes userinfo and <c>evil</c> the
    /// host, and control characters or tabs can be normalised away. An entry with a scheme,
    /// userinfo, a path, whitespace or control characters is refused, as is an IPv6 zone id,
    /// which could not be carried through to the dial.
    /// </remarks>
    /// <exception cref="FormatException">The entry is not <c>host[:port]</c>.</exception>
    public static NatsUri FromConnectUrl(string connectUrl, string scheme)
    {
        string host;
        string port;
        if (connectUrl.StartsWith("[", StringComparison.Ordinal))
        {
            // [IPv6] or [IPv6]:port
            var end = connectUrl.IndexOf(']');
            if (end < 2)
                throw new FormatException("Server URL is not host:port");

            host = connectUrl.Substring(0, end + 1);
            port = connectUrl.Substring(end + 1);
            for (var i = 1; i < end; i++)
            {
                var c = connectUrl[i];
                if (!IsHexDigit(c) && c != ':' && c != '.')
                    throw new FormatException("Server URL is not host:port");
            }

            if (port.Length > 0 && port[0] != ':')
                throw new FormatException("Server URL is not host:port");
        }
        else
        {
            var colon = connectUrl.IndexOf(':');
            host = colon < 0 ? connectUrl : connectUrl.Substring(0, colon);
            port = colon < 0 ? string.Empty : connectUrl.Substring(colon);
            if (host.Length == 0)
                throw new FormatException("Server URL is not host:port");

            // ASCII only: an internationalised name has to come as punycode
            foreach (var c in host)
            {
                if (!IsHexDigit(c) && c is not ((>= 'g' and <= 'z') or (>= 'G' and <= 'Z') or '.' or '-' or '_'))
                    throw new FormatException("Server URL is not host:port");
            }
        }

        if (port.Length > 0)
        {
            // ':' and 1 to 5 digits, 1-65535
            var value = 0;
            if (port.Length is < 2 or > 6)
                throw new FormatException("Server URL is not host:port");

            for (var i = 1; i < port.Length; i++)
            {
                if (port[i] is < '0' or > '9')
                    throw new FormatException("Server URL is not host:port");

                value = (value * 10) + (port[i] - '0');
            }

            if (value is < 1 or > 65535)
                throw new FormatException("Server URL is not host:port");
        }

        var uri = new NatsUri(host + port, false, scheme);

        // what was checked above must be what the URL parser made of it
        if (uri.Uri.Scheme != scheme || uri.Uri.UserInfo.Length != 0 || uri.Uri.AbsolutePath != "/" || uri.Uri.Query.Length != 0)
            throw new FormatException("Server URL is not host:port");

        return uri;

        static bool IsHexDigit(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
    }

    public override string ToString()
    {
        var uriBuilder = new UriBuilder(Uri);

        // Redact user/password or token from the URI string for logging
        if (uriBuilder.UserName is { Length: > 0 })
        {
            if (uriBuilder.Password is { Length: > 0 })
            {
                uriBuilder.Password = "***";
            }
            else
            {
                uriBuilder.UserName = "***";
            }
        }

        return IsWebSocket && Uri.AbsolutePath != "/" ? uriBuilder.Uri.ToString() : uriBuilder.Uri.ToString().Trim('/');
    }
}
