using System.Runtime.CompilerServices;

namespace NATS.Client.CoreUnit.Tests;

public class NatsMsgTests
{
    [Theory]
    [InlineData(42, NatsMsgFlags.None, false, false)]
    [InlineData(42, NatsMsgFlags.Empty, true, false)]
    [InlineData(42, NatsMsgFlags.NoResponders, false, true)]
    [InlineData(42, NatsMsgFlags.Empty | NatsMsgFlags.NoResponders, true, true)]
    [InlineData(1024 * 1024 * 128, NatsMsgFlags.Empty, true, false)]
    public void Size_and_flags(int size, NatsMsgFlags flags, bool isEmpty, bool hasNoResponders)
    {
        var msg = new NatsMsg<string> { Size = size, Flags = flags };
        Assert.Equal(size, msg.Size);
        Assert.Equal(flags, msg.Flags);
        Assert.Equal(isEmpty, msg.IsEmpty);
        Assert.Equal(hasNoResponders, msg.HasNoResponders);
    }

    [Fact]
    public void Check_struct_size()
    {
        Assert.Equal(48, Unsafe.SizeOf<NatsMsg<string>>());
    }
}
