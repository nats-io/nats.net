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

    [Fact]
    public async Task SignalDisconnected_DoesNotBlock_WhileInnerSocketIsDisposing()
    {
        // A WebSocket close completes the pending receive, so the read loop calls
        // SignalDisconnected while DisposeAsync is still awaiting the inner socket.
        // On a single-threaded host (Blazor WebAssembly) blocking there freezes the app.
        var innerDisposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var innerDisposeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var socket = new SocketConnectionWrapper(new FakeSocketConnection(innerDisposeStarted, innerDisposeGate.Task));

        var disposeTask = socket.DisposeAsync().AsTask();
        await WithTimeout(innerDisposeStarted.Task, TimeSpan.FromSeconds(10));

        try
        {
            var signalTask = Task.Run(() => socket.SignalDisconnected(new Exception("closed")));
            await WithTimeout(signalTask, TimeSpan.FromSeconds(5));
        }
        finally
        {
            innerDisposeGate.TrySetResult(true);
            await WithTimeout(disposeTask, TimeSpan.FromSeconds(10));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => socket.WaitForClosed);
    }

    // Task.WaitAsync is not available on net481.
    private static async Task WithTimeout(Task task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException("The operation has timed out.");
        }

        await task;
    }

    private class FakeSocketConnection(TaskCompletionSource<bool>? disposeStarted = null, Task? disposeGate = null) : INatsSocketConnection
    {
        public async ValueTask DisposeAsync()
        {
            disposeStarted?.TrySetResult(true);
            if (disposeGate != null)
            {
                await disposeGate;
            }
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer) => throw new NotImplementedException();

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer) => throw new NotImplementedException();
    }
}
