using System.Reflection;
using System.Text.Json.Serialization;

namespace NATS.Client.Core.Internal;

// SYSLIB1037: The type 'ClientOpts' defines init-only properties,
// deserialization of which is currently not supported in source generation mode
#pragma warning disable SYSLIB1037

// These connections options are serialized and sent to the server.
// https://github.com/nats-io/nats-server/blob/a23b1b7/server/client.go#L536
internal sealed class ClientOpts
{
    private ClientOpts(NatsOpts opts)
    {
        Name = opts.Name;
        Echo = opts.Echo;
        Verbose = opts.Verbose;
        Headers = opts.Headers;
        Username = opts.AuthOpts.Username;
        Password = opts.AuthOpts.Password;
        AuthToken = opts.AuthOpts.Token;
        JWT = opts.AuthOpts.Jwt;
    }

    /// <summary>Optional boolean. If set to true, the server (version 1.2.0+) will not send originating messages from this connection to its own subscriptions. Clients should set this to true only for server supporting this feature, which is when proto in the INFO protocol is set to at least 1.</summary>
    [JsonPropertyName("echo")]
    public bool Echo { get; init; } = true;

    /// <summary>Turns on +OK protocol acknowledgements.</summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; }

    /// <summary>Turns on additional strict format checking, e.g. for properly formed subjects</summary>
    [JsonPropertyName("pedantic")]
    public bool Pedantic { get; init; }

    /// <summary>Indicates whether the client requires an SSL connection.</summary>
    [JsonPropertyName("tls_required")]
    public bool TLSRequired { get; init; }

    [JsonPropertyName("nkey")]
    public string? NKey { get; set; }

    /// <summary>The JWT that identifies a user permissions and account.</summary>
    [JsonPropertyName("jwt")]
    public string? JWT { get; set; } = null;

    /// <summary>In case the server has responded with a nonce on INFO, then a NATS client must use this field to reply with the signed nonce.</summary>
    [JsonPropertyName("sig")]
    public string? Sig { get; set; } = null;

    /// <summary>Client authorization token (if auth_required is set)</summary>
    [JsonPropertyName("auth_token")]
    public string? AuthToken { get; set; }

    /// <summary>Connection username (if auth_required is set)</summary>
    [JsonPropertyName("user")]
    public string? Username { get; init; } = null;

    /// <summary>Connection password (if auth_required is set)</summary>
    [JsonPropertyName("pass")]
    public string? Password { get; init; } = null;

    /// <summary>Optional client name</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; } = null;

    /// <summary>The implementation language of the client.</summary>
    [JsonPropertyName("lang")]
    public string ClientLang { get; init; } = "C#";

    /// <summary>The version of the client.</summary>
    [JsonPropertyName("version")]
    public string ClientVersion { get; init; } = GetAssemblyVersion();

    /// <summary>optional int. Sending 0 (or absent) indicates client supports original protocol. Sending 1 indicates that the client supports dynamic reconfiguration of cluster topology changes by asynchronously receiving INFO messages with known servers it can reconnect to.</summary>
    [JsonPropertyName("protocol")]
    public int Protocol { get; init; } = 1;

    [JsonPropertyName("account")]
    public string? Account { get; init; } = null;

    [JsonPropertyName("new_account")]
    public bool? AccountNew { get; init; }

    [JsonPropertyName("headers")]
    public bool Headers { get; init; } = false;

    [JsonPropertyName("no_responders")]
    public bool NoResponders { get; init; } = false;

    public static ClientOpts Create(NatsOpts opts)
    {
        return new ClientOpts(opts);
    }

    private static string GetAssemblyVersion()
    {
        var asm = typeof(ClientOpts);
        var version = "1.0.0";
        var infoVersion = asm!.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (infoVersion != null)
        {
            version = infoVersion.InformationalVersion;
        }
        else
        {
            var asmVersion = asm!.GetCustomAttribute<AssemblyVersionAttribute>();
            if (asmVersion != null)
            {
                version = asmVersion.Version;
            }
        }

        return version;
    }
}

#pragma warning restore SYSLIB1037
