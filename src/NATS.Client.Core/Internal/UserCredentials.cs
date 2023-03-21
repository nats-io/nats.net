using System.Text;
using NATS.Client.Core.NaCl;

namespace NATS.Client.Core.Internal;

internal class UserCredentials
{
    public UserCredentials(NatsAuthOptions authOptions)
    {
        Jwt = authOptions.Jwt;
        Seed = authOptions.Seed;
        Nkey = authOptions.Nkey;
        Token = authOptions.Token;

        if (!string.IsNullOrEmpty(authOptions.CredsFile))
        {
            (Jwt, Seed) = LoadCredsFile(authOptions.CredsFile);
        }

        if (!string.IsNullOrEmpty(authOptions.NKeyFile))
        {
            (Seed, Nkey) = LoadNKeyFile(authOptions.NKeyFile);
        }
    }

    public string? Jwt { get; }

    public string? Seed { get; }

    public string? Nkey { get; }

    public string? Token { get; }

    public string? Sign(string? nonce)
    {
        if (Seed == null || nonce == null) return null;

        using var kp = Nkeys.FromSeed(Seed);
        byte[] bytes = kp.Sign(Encoding.ASCII.GetBytes(nonce));
        var sig = CryptoBytes.ToBase64String(bytes);

        return sig;
    }

    internal void Authenticate(ClientOptions options, ServerInfo? info)
    {
        options.JWT = Jwt;
        options.Nkey = Nkey;
        options.AuthToken = Token;
        options.Sig = info is { AuthRequired: true, Nonce: { } } ? Sign(info.Nonce) : null;
    }

    private (string, string) LoadCredsFile(string path)
    {
        string? jwt = null;
        string? seed = null;
        using var reader = new StreamReader(path);
        while (reader.ReadLine()?.Trim() is { } line)
        {
            if (line.StartsWith("-----BEGIN NATS USER JWT-----"))
            {
                jwt = reader.ReadLine();
                if (jwt == null) break;
            }
            else if (line.StartsWith("-----BEGIN USER NKEY SEED-----"))
            {
                seed = reader.ReadLine();
                if (seed == null) break;
            }
        }

        if (jwt == null)
            throw new NatsException($"Can't find JWT while loading creds file ${path}");
        if (seed == null)
            throw new NatsException($"Can't find NKEY seed while loading creds file ${path}");

        return (jwt, seed);
    }

    private (string, string) LoadNKeyFile(string path)
    {
        string? seed = null;
        string? nkey = null;

        using var reader = new StreamReader(path);
        while (reader.ReadLine()?.Trim() is { } line)
        {
            if (line.StartsWith("SU"))
            {
                seed = line;
            }
            else if (line.StartsWith("U"))
            {
                nkey = line;
            }
        }

        if (seed == null)
            throw new NatsException($"Can't find seed while loading NKEY file ${path}");
        if (nkey == null)
            throw new NatsException($"Can't find public key while loading NKEY file ${path}");

        return (seed, nkey);
    }
}
