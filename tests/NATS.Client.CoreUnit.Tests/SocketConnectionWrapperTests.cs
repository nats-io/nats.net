namespace NATS.Client.CoreUnit.Tests;

public class SocketConnectionWrapperTests
{
    [Fact]
    public async Task SignalDisconnected_DoesNotCause_UnobservedException()
    {
        // Arrange
        var exceptionThrown = false;
        var socketConnection = new FakeSocketConnection();
        var socket = new SocketConnectionWrapper(socketConnection);

        // Act
        TaskScheduler.UnobservedTaskException += (_, _) =>
        {
            exceptionThrown = true;
        };

        socket.SignalDisconnected(new Exception("test"));
        await socket.DisposeAsync();
        socket = null;

        await Task.Delay(100);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Assert
        Assert.False(exceptionThrown);
    }

    internal class FakeSocketConnection : INatsSocketConnection
    {
        public ValueTask DisposeAsync() => default;

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => throw new NotImplementedException();

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => throw new NotImplementedException();
    }
}
