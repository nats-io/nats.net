using System.Text;
using NATS.Client.Core.NaCl;

namespace NATS.Client.Core;

public class UserCredentials
{
    public static readonly UserCredentials Anonymous = new(null, null);

    public UserCredentials(string? jwt, NkeyPair? nkeyPair)
    {
        Jwt = jwt;
        NkeyPair = nkeyPair;
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

        NkeyPair? nkeyPair = seed != null ? Nkeys.FromSeed(seed) : null;

        return new UserCredentials(jwt, nkeyPair);
    }

    public string? Jwt { get; }

    public NkeyPair? NkeyPair { get; }

    public string? Sign(string? nonce)
    {
        if (NkeyPair == null || nonce == null) return null;
        byte[] bytes = NkeyPair.Sign(Encoding.ASCII.GetBytes(nonce));
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
