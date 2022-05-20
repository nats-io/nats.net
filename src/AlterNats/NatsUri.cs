namespace AlterNats;

internal sealed class NatsUri : IEquatable<NatsUri>
{
    const string DefaultScheme = "nats://";
    public static readonly NatsUri Default = new NatsUri("nats://localhost:4222");
    public const int DefaultPort = 4222;

    internal readonly Uri Uri;
    public bool IsSecure { get; }
    public bool IsWebSocket { get; }
    public string Host => Uri.Host;
    public int Port => Uri.Port;

    public NatsUri(string urlString)
    {
        if (!urlString.Contains("://"))
        {
            urlString = DefaultScheme + urlString;
        }

        this.Uri = new Uri(urlString);
        if (Uri.Scheme is "tls" or "wss")
        {
            IsSecure = true;
        }

        if (Uri.Scheme is "ws" or "wss")
        {
            IsWebSocket = true;
        }
    }

    public override string ToString()
    {
        return Uri.ToString().Trim('/');
    }

    public override int GetHashCode() => Uri.GetHashCode();

    public bool Equals(NatsUri? other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Uri.Equals(other.Uri);
    }
}
