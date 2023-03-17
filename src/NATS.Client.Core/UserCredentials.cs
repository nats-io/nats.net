using System.Text;
using NATS.Client.Core.NaCl;

namespace NATS.Client.Core;

public class UserCredentials
{
    public static readonly UserCredentials Anonymous = new(null, null);

    public UserCredentials(string? jwt, string? seed)
    {
        Jwt = jwt;
        Seed = seed;
    }

    public static UserCredentials LoadFromFile(string path)
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
            else if (line.StartsWith("SU"))
            {
                seed = line;
            }
        }

        return new UserCredentials(jwt, seed);
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

    internal ConnectOptions Authenticate(ConnectOptions options, ServerInfo? info)
    {
        if (info is not { AuthRequired: true }) return options;

        return options with {
            Sig = Sign(info.Nonce),
            JWT = Jwt,
        };
    }
}
