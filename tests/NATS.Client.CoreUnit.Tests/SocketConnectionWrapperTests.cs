namespace NATS.Client.CoreUnit.Tests;

public class SocketConnectionWrapperTests
{
    [Fact]
    public async Task SignalDisconnected_DoesNotCause_UnobservedException()
    {
        // Flush any pending unobserved exceptions from other tests or framework
        // code so they don't cause false positives when we GC.Collect below.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Task.Delay(100);

        // Arrange
        var exceptionThrown = false;
        var socketConnection = new FakeSocketConnection();
        var socket = new SocketConnectionWrapper(socketConnection);

        void Handler(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            exceptionThrown = true;
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            // Act
            socket.SignalDisconnected(new Exception("test"));
            await socket.DisposeAsync();
            socket = null;
            socketConnection = null;

            await Task.Delay(100);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert
            Assert.False(exceptionThrown);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }

    private class FakeSocketConnection : INatsSocketConnection
    {
        public ValueTask DisposeAsync() => default;

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => throw new NotImplementedException();

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => throw new NotImplementedException();
    }
}
