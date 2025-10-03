using NATS.Client.Core.Tests;

namespace NATS.Client.Core2.Tests;

public class SemVerTest
{
    [Fact]
    public void TestSemVerParsing()
    {
        var server = new ServerInfo { Version = "2.12.0" };
        var test = "2.11.0";
        Assert.True(server.VersionIsGreaterThenOrEqualTo(test));
    }

    [Fact]
    public void TestSemVerMajorMinor()
    {
        var server = new ServerInfo { Version = "2.12.0-preview.1" };
        var test = "2.12.0";
        Assert.False(server.VersionIsGreaterThenOrEqualTo(test));
        Assert.True(server.VersionMajorMinorIsGreaterThenOrEqualTo(2, 12));
    }
}
