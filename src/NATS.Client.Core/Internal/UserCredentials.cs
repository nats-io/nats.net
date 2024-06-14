using System.Text;
using NATS.Client.Core.NaCl;

namespace NATS.Client.Core.Internal;

internal class UserCredentials
{
    public UserCredentials(NatsAuthOpts authOpts)
    {
        Jwt = authOpts.Jwt;
        Seed = authOpts.Seed;
        NKey = authOpts.NKey;
        Token = authOpts.Token;

        if (!string.IsNullOrEmpty(authOpts.CredsFile))
        {
            (Jwt, Seed) = LoadCredsFile(authOpts.CredsFile);
        }

        if (!string.IsNullOrEmpty(authOpts.NKeyFile))
        {
            (Seed, NKey) = LoadNKeyFile(authOpts.NKeyFile);
        }
    }

    public string? Jwt { get; }

    public string? Seed { get; }

    public string? NKey { get; }

    public string? Token { get; }

    public string? Sign(string? nonce)
    {
        if (Seed == null || nonce == null)
            return null;

        using var kp = NKeys.FromSeed(Seed);
        var bytes = kp.Sign(Encoding.ASCII.GetBytes(nonce));
        var sig = CryptoBytes.ToBase64String(bytes);

        return sig;
    }

    internal void Authenticate(ClientOpts opts, ServerInfo? info)
    {
        opts.JWT = Jwt;
        opts.NKey = NKey;
        opts.AuthToken = Token;
        opts.Sig = info is { AuthRequired: true, Nonce: { } } ? Sign(info.Nonce) : null;
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
                if (jwt == null)
                    break;
            }
            else if (line.StartsWith("-----BEGIN USER NKEY SEED-----"))
            {
                seed = reader.ReadLine();
                if (seed == null)
                    break;
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
