using System.Security.Cryptography;
using System.Text;
using NATS.Client.ObjectStore.Internal;

namespace NATS.Client.ObjectStore.Encoder.Tests;

public class Base64UrlEncoderTest
{
    private readonly ITestOutputHelper _output;

    public Base64UrlEncoderTest(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("Hello World!")]
    [InlineData("!~£$%^&*()_+{}:@~<>?")]
    [InlineData("C")]
    [InlineData("AB")]
    [InlineData("ABC")]
    public void Encoding_test(string input)
    {
        var encoded = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(input));
        var expected = Encode(input);
        Assert.Equal(expected, encoded);
        _output.WriteLine($">>{encoded}<<");
    }

    [Theory]
    [InlineData("SGVsbG8gV29ybGQh")]
    [InlineData("IX7CoyQlXiYqKClfK3t9OkB-PD4_")]
    [InlineData("Qw==")]
    [InlineData("QUI=")]
    [InlineData("QUJD")]
    public void Decoding_test(string input)
    {
        var decoded = Base64UrlEncoder.Decode(input);
        var expected = Decode(input);
        Assert.Equal(expected, decoded);
        _output.WriteLine($">>{decoded}<<");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hello World!")]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    public void Sha256_test(string input)
    {
        var data = Encoding.UTF8.GetBytes(input);
        var actual = Base64UrlEncoder.Sha256(data);

        // Independent reference: hash, then base64url-encode the 32-byte digest keeping the '=' pad.
        using var sha256 = SHA256.Create();
        var expected = Convert.ToBase64String(sha256.ComputeHash(data))
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expected, actual);
        _output.WriteLine($">>{actual}<<");
    }

    private string Encode(string input, bool raw = false)
    {
        var base64String = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));

        if (raw)
        {
            base64String = base64String.TrimEnd('=');
        }

        return base64String
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private string Decode(string input)
    {
        var incoming = input
            .Replace('_', '/')
            .Replace('-', '+');

        switch (input.Length % 4)
        {
        case 2:
            incoming += "==";
            break;
        case 3:
            incoming += "=";
            break;
        }

        var bytes = Convert.FromBase64String(incoming);

        return Encoding.UTF8.GetString(bytes);
    }
}
