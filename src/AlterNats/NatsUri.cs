namespace AlterNats;

internal sealed class NatsUri : IEquatable<NatsUri>
{
    public const string DefaultScheme = "nats";

    public readonly Uri Uri;
    public readonly bool IsSecure;
    public readonly bool IsWebSocket;
    public string Host => Uri.Host;
    public int Port => Uri.Port;

    public NatsUri(string urlString, string defaultScheme = DefaultScheme)
    {
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
            case "nats":
                if (uriBuilder.Port == -1)
                {
                    uriBuilder.Port = 4222;
                }
                break;
            case "ws":
                IsWebSocket = true;
                break;
            case "wss":
                IsWebSocket = true;
                IsSecure = true;
                break;
            default:
                throw new ArgumentException($"unsupported scheme {uriBuilder.Scheme} in nats URL {urlString}", urlString);
        }

        Uri = uriBuilder.Uri;
    }

    public override string ToString()
    {
        return IsWebSocket && Uri.AbsolutePath != "/" ? Uri.ToString() : Uri.ToString().Trim('/');
    }

    public override int GetHashCode() => Uri.GetHashCode();

    public bool Equals(NatsUri? other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Uri.Equals(other.Uri);
    }
}
