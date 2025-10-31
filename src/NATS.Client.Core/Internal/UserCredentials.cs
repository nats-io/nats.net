using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
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
        AuthCredCallback = authOpts.AuthCredCallback;

#if NETSTANDARD2_0
#pragma warning disable CS8604 // Possible null reference argument.
#endif
        if (!string.IsNullOrEmpty(authOpts.CredsFile))
        {
            (Jwt, Seed) = LoadCredsFile(authOpts.CredsFile);
        }

        if (!string.IsNullOrEmpty(authOpts.Creds))
        {
            (Jwt, Seed) = LoadCredsContent(authOpts.Creds);
        }

        if (!string.IsNullOrEmpty(authOpts.NKeyFile))
        {
            (Seed, NKey) = LoadNKeyFile(authOpts.NKeyFile);
        }
#if NETSTANDARD2_0
#pragma warning restore CS8604 // Possible null reference argument.
#endif
    }

    public string? Jwt { get; }

    public string? Seed { get; }

    public string? NKey { get; }

    public string? Token { get; }

    public Func<Uri, CancellationToken, ValueTask<NatsAuthCred>>? AuthCredCallback { get; }

    public string? Sign(string? nonce, string? seed = null)
    {
        seed ??= Seed;

        if (seed == null || nonce == null)
            return null;

        using var kp = NKeys.FromSeed(seed);
        var bytes = kp.Sign(Encoding.ASCII.GetBytes(nonce));
        var sig = CryptoBytes.ToBase64String(bytes);

        return sig;
    }

    internal async Task AuthenticateAsync(ClientOpts opts, ServerInfo? info, NatsUri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string? seed = null;
        if (AuthCredCallback != null)
        {
            using var cts = new CancellationTokenSource(timeout);
#if NETSTANDARD
            using var ctr = cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), cts);
#else
            await using var ctr = cancellationToken.UnsafeRegister(static state => ((CancellationTokenSource)state!).Cancel(), cts);
#endif
            var authCred = await AuthCredCallback(uri.Uri, cts.Token).ConfigureAwait(false);

            switch (authCred.Type)
            {
            case NatsAuthType.None:
                // Behavior in this case is undefined.
                // A follow-up PR should define the AuthCredCallback
                // behavior when returning NatsAuthType.None.
                break;
            case NatsAuthType.UserInfo:
                opts.Username = authCred.Value;
                opts.Password = authCred.Secret;
                break;
            case NatsAuthType.Token:
                opts.AuthToken = authCred.Value;
                break;
            case NatsAuthType.Jwt:
                opts.JWT = authCred.Value;
                seed = authCred.Secret;
                break;
#if NETSTANDARD2_0
#pragma warning disable CS8604 // Possible null reference argument for parameter...
#endif
            case NatsAuthType.Nkey:
                if (!string.IsNullOrEmpty(authCred.Secret))
                {
                    seed = authCred.Secret;
                    opts.NKey = NKeys.PublicKeyFromSeed(seed);
                }

                break;
            case NatsAuthType.Creds:
                if (!string.IsNullOrEmpty(authCred.Value))
                {
                    (opts.JWT, seed) = LoadCredsContent(authCred.Value);
                }

                break;
            case NatsAuthType.CredsFile:
                if (!string.IsNullOrEmpty(authCred.Value))
                {
                    (opts.JWT, seed) = LoadCredsFile(authCred.Value);
                }

                break;
            case NatsAuthType.NkeyFile:
                if (!string.IsNullOrEmpty(authCred.Value))
                {
                    (seed, opts.NKey) = LoadNKeyFile(authCred.Value);
                }

                break;
            }
#if NETSTANDARD2_0
#pragma warning restore CS8604 // Possible null reference argument for parameter...
#endif
        }
        else
        {
            opts.JWT = Jwt;
            opts.NKey = NKey;
            opts.AuthToken = Token;
        }

        opts.Sig = info is { AuthRequired: true, Nonce: { } } ? Sign(info.Nonce, seed) : null;
    }

    private (string, string) LoadCredsContent(string creds)
    {
        try
        {
            using var reader = new StringReader(creds);
            return ParseCreds(reader);
        }
        catch (NatsException e)
        {
            throw new NatsException("Error loading credentials", e);
        }
    }

    private (string, string) LoadCredsFile(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            return ParseCreds(reader);
        }
        catch (NatsException e)
        {
            throw new NatsException($"Error loading creds file '{path}': {e.Message}", e);
        }
    }

    private (string, string) ParseCreds(TextReader reader)
    {
        string? jwt = null;
        string? seed = null;
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
            throw new NatsException($"Can't find JWT in credentials");
        if (seed == null)
            throw new NatsException($"Can't find NKEY in credentials");

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
