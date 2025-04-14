namespace NATS.Client.Core.Internal;

internal sealed record NatsUri
{
    public const string DefaultScheme = "nats";

    private readonly string _redacted;

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

        _redacted = IsWebSocket && Uri.AbsolutePath != "/" ? uriBuilder.Uri.ToString() : uriBuilder.Uri.ToString().Trim('/');
    }

    public Uri Uri { get; init; }

    public bool IsSeed { get; }

    public bool IsTls { get; }

    public bool IsWebSocket { get; }

    public string Host => Uri.Host;

    public int Port => Uri.Port;

    public override string ToString() => _redacted;
}
