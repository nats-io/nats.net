using NATS.Client.Core.NaCl;

namespace NATS.Client.Core.Tests;

public class NKeyTests
{
    [Fact]
    public void NKey_Seed_And_Sign()
    {
        const string seed = "SUAAVWRZG6M5FA5VRRGWSCIHKTOJC7EWNIT4JV3FTOIPO4OBFR5WA7X5TE";
        const string expectedSignedResult = "ScXMdbqkD6/9G4/Z/lsAingexP9sfeGLZMdFe4C9oqGy21VtygL4mJRpCI0NCKC8OgfgV2XObqFB8ZnC+exdAw==";
        const string expectedPublicKey = "Fwky8ZAB/N0GGNIU6S7CawRZm1wqNnVYQ83CP2UBk88=";
        var dataToSign = "Hello World"u8;

        var nKey = NKeys.FromSeed(seed);

        // Sanity check public key
        var actualPublicKey = Convert.ToBase64String(nKey.PublicKey);
        Assert.Equal(expectedPublicKey, actualPublicKey);

        // Sign and verify
        var signedData = nKey.Sign(dataToSign.ToArray());
        var actual = Convert.ToBase64String(signedData);

        Assert.Equal(expectedSignedResult, actual);
    }

    [Fact]
    public void Sha512_Hash()
    {
        var dataToHash = "Can I Haz Hash please?"u8;
        const string ExpectedHash = "uLV1BK1SKsQ69Sy4a7ENMVhAx9G4C986JSRlT3wsOwfGAa3TIOn4cKb6jaMAPPob4zATPQq+185J+SUdK7l0IQ==";

        var dataArray = dataToHash.ToArray();
        var actual = Convert.ToBase64String(Sha512.Hash(dataArray, 0, dataArray.Length));
        Assert.Equal(ExpectedHash, actual);
    }
}
