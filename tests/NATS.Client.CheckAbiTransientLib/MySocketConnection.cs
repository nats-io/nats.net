using System.Net.Sockets;
using NATS.Client.Core;

namespace NATS.Client.CheckAbiTransientLib;

/// <summary>
/// Compiled against NATS.Net 2.6.0 where INatsSocketConnection and
/// INatsTlsUpgradeableSocketConnection live in NATS.Client.Core. When used with a newer
/// build, type forwarding should redirect both to NATS.Client.Abstractions.
/// </summary>
public class MySocketConnection : INatsTlsUpgradeableSocketConnection
{
    public Socket Socket => throw new NotSupportedException();

    public static string GetSocketConnectionInterfaceAssembly()
    {
        return typeof(INatsSocketConnection).Assembly.GetName().Name!;
    }

    public static string GetTlsUpgradeableInterfaceAssembly()
    {
        return typeof(INatsTlsUpgradeableSocketConnection).Assembly.GetName().Name!;
    }

    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => new(buffer.Length);

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => new(0);

    public ValueTask DisposeAsync() => default;
}
