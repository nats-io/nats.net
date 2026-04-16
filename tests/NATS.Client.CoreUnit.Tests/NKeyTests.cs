using System.Text;
using NATS.NKeys;

namespace NATS.Client.Core.Tests;

public class NKeyTests
{
    [Fact]
    public void NKey_Seed_And_Sign()
    {
        const string seed = "SUAAVWRZG6M5FA5VRRGWSCIHKTOJC7EWNIT4JV3FTOIPO4OBFR5WA7X5TE";
        const string expectedPublicKey = "UALQSMXRSAA7ZXIGDDJBJ2JOYJVQIWM3LQVDM5KYIPG4EP3FAGJ47BOJ";
        const string expectedSignedResult = "49C5CC75BAA40FAFFD1B8FD9FE5B008A781EC4FF6C7DE18B64C7457B80BDA2A1B2DB556DCA02F8989469088D0D08A0BC3A07E05765CE6EA141F199C2F9EC5D03";
        var dataToSign = "Hello World"u8;

        using var kp = KeyPair.FromSeed(seed.AsSpan());

        // Sanity check public key
        Assert.Equal(expectedPublicKey, kp.GetPublicKey());

        // Sign and verify
        var signature = new byte[64];
        kp.Sign(dataToSign.ToArray(), signature);
        Assert.Equal(expectedSignedResult, ToHexString(signature));
    }

    private static string ToHexString(byte[] bytes)
    {
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            hex.Append($"{b:X2}");
        }

        return hex.ToString();
    }
}
