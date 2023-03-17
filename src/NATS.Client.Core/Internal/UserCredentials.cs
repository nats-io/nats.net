using System.Text;
using NATS.Client.Core.NaCl;

namespace NATS.Client.Core.Internal;

internal class UserCredentials
{
    public UserCredentials(NatsAuthOptions authOptions)
    {
        Jwt = authOptions.Jwt;
        Seed = authOptions.Seed;
        if (!string.IsNullOrEmpty(authOptions.CredsFile))
        {
            (Jwt, Seed) = LoadCredsFile(authOptions.CredsFile);
        }
        if (!string.IsNullOrEmpty(authOptions.NKeyFile))
        {
            Seed = LoadNKeyFile(authOptions.NKeyFile);
        }
    }

    (string, string) LoadCredsFile(string path)
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

    string LoadNKeyFile(string path)
    {
        string? seed = null;

        using var reader = new StreamReader(path);
        while (reader.ReadLine()?.Trim() is { } line)
        {
            if (line.StartsWith("SU"))
            {
                seed = line;
            }
        }

        if (seed == null)
            throw new NatsException($"Can't find NKEY seed while loading creds file ${path}");

        return seed;
    }

    public string? Jwt { get; }

    public string? Seed { get; }

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
        options.Sig = info is { AuthRequired: true, Nonce: { } } ? Sign(info.Nonce) : null;
    }
}
