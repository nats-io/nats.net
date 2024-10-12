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
        // Using https://github.com/SergeyTeplyakov/ObjectLayoutInspector
        /* Size: 48 bytes. Paddings: 4 bytes (%8 of empty space)
        |==============================================================|
        |   0-7: String <Subject>k__BackingField (8 bytes)             |
        |--------------------------------------------------------------|
        |  8-15: String <ReplyTo>k__BackingField (8 bytes)             |
        |--------------------------------------------------------------|
        | 16-23: NatsHeaders <Headers>k__BackingField (8 bytes)        |
        |--------------------------------------------------------------|
        | 24-31: String <Data>k__BackingField (8 bytes)                |
        |--------------------------------------------------------------|
        | 32-39: INatsConnection <Connection>k__BackingField (8 bytes) |
        |--------------------------------------------------------------|
        | 40-43: UInt32 _flagsAndSize (4 bytes)                        |
        |--------------------------------------------------------------|
        | 44-47: padding (4 bytes)                                     |
        |==============================================================| */
        Assert.Equal(48, Unsafe.SizeOf<NatsMsg<string>>());

        /* Size: 40 bytes. Paddings: 0 bytes (%0 of empty space)
        |==============================================================|
        |   0-7: String <Subject>k__BackingField (8 bytes)             |
        |--------------------------------------------------------------|
        |  8-15: String <ReplyTo>k__BackingField (8 bytes)             |
        |--------------------------------------------------------------|
        | 16-23: NatsHeaders <Headers>k__BackingField (8 bytes)        |
        |--------------------------------------------------------------|
        | 24-31: INatsConnection <Connection>k__BackingField (8 bytes) |
        |--------------------------------------------------------------|
        | 32-35: UInt32 _flagsAndSize (4 bytes)                        |
        |--------------------------------------------------------------|
        | 36-39: Int32 <Data>k__BackingField (4 bytes)                 |
        |==============================================================| */
        Assert.Equal(40, Unsafe.SizeOf<NatsMsg<int>>());

        /* Size: 40 bytes. Paddings: 3 bytes (%7 of empty space)
        |==============================================================|
        |   0-7: String <Subject>k__BackingField (8 bytes)             |
        |--------------------------------------------------------------|
        |  8-15: String <ReplyTo>k__BackingField (8 bytes)             |
        |--------------------------------------------------------------|
        | 16-23: NatsHeaders <Headers>k__BackingField (8 bytes)        |
        |--------------------------------------------------------------|
        | 24-31: INatsConnection <Connection>k__BackingField (8 bytes) |
        |--------------------------------------------------------------|
        | 32-35: UInt32 _flagsAndSize (4 bytes)                        |
        |--------------------------------------------------------------|
        |    36: Byte <Data>k__BackingField (1 byte)                   |
        |--------------------------------------------------------------|
        | 37-39: padding (3 bytes)                                     |
        |==============================================================| */
        Assert.Equal(40, Unsafe.SizeOf<NatsMsg<byte>>());
    }
}
