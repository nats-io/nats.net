using NATS.Client.Core.NaCl;

namespace NATS.Client.Core.Tests;

public class NKeyTests
{
    [Fact]
    public void NKey_Seed_And_Sign()
    {
        const string seed = "SUAAVWRZG6M5FA5VRRGWSCIHKTOJC7EWNIT4JV3FTOIPO4OBFR5WA7X5TE";
        const string expectedSignedResult = "49C5CC75BAA40FAFFD1B8FD9FE5B008A781EC4FF6C7DE18B64C7457B80BDA2A1B2DB556DCA02F8989469088D0D08A0BC3A07E05765CE6EA141F199C2F9EC5D03";
        const string expectedPublicKey = "170932F19001FCDD0618D214E92EC26B04599B5C2A36755843CDC23F650193CF";
        var dataToSign = "Hello World"u8;

        var nKey = NKeys.FromSeed(seed);

        // Sanity check public key
        var actualPublicKey = Convert.ToHexString(nKey.PublicKey);
        Assert.Equal(expectedPublicKey, actualPublicKey);

        // Sign and verify
        var signedData = nKey.Sign(dataToSign.ToArray());
        var actual = Convert.ToHexString(signedData);

        Assert.Equal(expectedSignedResult, actual);
    }

    [Fact]
    public void Sha512_Hash()
    {
        var dataToHash = "Can I Haz Hash please?"u8;
        const string ExpectedHash = "B8B57504AD522AC43AF52CB86BB10D315840C7D1B80BDF3A2524654F7C2C3B07C601ADD320E9F870A6FA8DA3003CFA1BE330133D0ABED7CE49F9251D2BB97421";

        var dataArray = dataToHash.ToArray();
        var actual = Convert.ToHexString(Sha512.Hash(dataArray, 0, dataArray.Length));

        Assert.Equal(ExpectedHash, actual);
    }
}
