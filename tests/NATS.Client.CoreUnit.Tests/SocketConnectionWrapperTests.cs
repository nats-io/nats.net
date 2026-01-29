namespace NATS.Client.CoreUnit.Tests;

public class SocketConnectionWrapperTests
{
    [Fact]
    public async Task SignalDisconnected_DoesNotCause_UnobservedException()
    {
        // Arrange
        var sentinel = "SocketConnectionWrapperTests_" + Guid.NewGuid().ToString("N");
        var unobservedException = default(AggregateException);
        var socketConnection = new FakeSocketConnection();
        var socket = new SocketConnectionWrapper(socketConnection);

        void Handler(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            // Only track exceptions from our code; ignore unrelated unobserved
            // exceptions from the runtime, xUnit, or other tests.
            if (args.Exception?.InnerExceptions.Any(e => e.Message == sentinel) == true)
            {
                unobservedException = args.Exception;
            }
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            // Act
            socket.SignalDisconnected(new Exception(sentinel));
            await socket.DisposeAsync();
            socket = null;
            socketConnection = null;

            await Task.Delay(100);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert
            Assert.Null(unobservedException);
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
