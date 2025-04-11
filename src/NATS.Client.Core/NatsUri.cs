namespace NATS.Client.Core;

public sealed class NatsUri : IEquatable<NatsUri>
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

    public Uri Uri { get; }

    public bool IsSeed { get; }

    public bool IsTls { get; }

    public bool IsWebSocket { get; }

    public string Host => Uri.Host;

    public int Port => Uri.Port;

    public NatsUri CloneWith(string host, int? port = default)
    {
        var newUri = new UriBuilder(Uri)
        {
            Host = host,
            Port = port ?? Port,
        }.Uri.ToString();

        return new NatsUri(newUri, IsSeed);
    }

    public override string ToString() => _redacted;

    public override int GetHashCode() => Uri.GetHashCode();

    public bool Equals(NatsUri? other)
    {
        if (other == null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Uri.Equals(other.Uri);
    }
}
