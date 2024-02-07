using System.Reflection;

namespace NATS.Client.JetStream.Tests;

public class NatsJSContextTest
{
    [Fact]
    public void InterfaceShouldHaveSamePublicPropertiesEventsAndMethodAsClass()
    {
        var classType = typeof(NatsJSContext);
        var interfaceType = typeof(INatsJSContext);
        var ignoredMethods = new List<string>
        {
            "GetType",
            "ToString",
            "Equals",
            "GetHashCode",
        };

        var classMethods = classType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !ignoredMethods.Contains(m.Name)).ToList();
        var interfaceMethods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            .Concat(interfaceType.GetInterfaces().SelectMany(i => i.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))).ToList();

        foreach (var classInfo in classMethods)
        {
            var name = classInfo.Name;
            interfaceMethods.Select(m => m.Name).Should().Contain(name);
        }
    }

    [Fact]
    public void Invalid_stream_validation_test()
    {
        Assert.Throws<ArgumentNullException>(() => NatsJSContext.ThrowIfInvalidStreamName(null!));
        Assert.Throws<ArgumentException>(() => NatsJSContext.ThrowIfInvalidStreamName("Invalid.DotName"));
        Assert.Throws<ArgumentException>(() => NatsJSContext.ThrowIfInvalidStreamName("Invalid SpaceName"));
        Assert.Throws<ArgumentException>(() => NatsJSContext.ThrowIfInvalidStreamName("Invalid*StarName"));
        Assert.Throws<ArgumentException>(() => NatsJSContext.ThrowIfInvalidStreamName("Invalid>WildcardName"));
    }
}
