namespace NATS.Client.Core.Tests;

public class PingCommandTest
{
    [Fact]
    public async Task SetResult_DeliversElapsedTime()
    {
        var cmd = new PingCommand(pool: null);
        cmd.Start();
        await Task.Delay(50);
        cmd.SetResult();

        var elapsed = await cmd.RunAsync();
        elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SetCanceled_ThrowsOperationCanceledException()
    {
        var cmd = new PingCommand(pool: null);
        cmd.Start();
        cmd.SetCanceled();

        var act = () => cmd.RunAsync().AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void SetResult_CalledTwice_DoesNotThrow()
    {
        var cmd = new PingCommand(pool: null);
        cmd.Start();
        cmd.SetResult();
        cmd.SetResult(); // second call should be a no-op
    }

    [Fact]
    public void SetCanceled_CalledTwice_DoesNotThrow()
    {
        var cmd = new PingCommand(pool: null);
        cmd.Start();
        cmd.SetCanceled();
        cmd.SetCanceled(); // second call should be a no-op
    }

    [Fact]
    public async Task SetResult_AfterSetCanceled_StillThrows()
    {
        var cmd = new PingCommand(pool: null);
        cmd.Start();
        cmd.SetCanceled();
        cmd.SetResult(); // should be ignored

        var act = () => cmd.RunAsync().AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SetCanceled_AfterSetResult_StillReturnsResult()
    {
        var cmd = new PingCommand(pool: null);
        cmd.Start();
        cmd.SetResult();
        cmd.SetCanceled(); // should be ignored

        var elapsed = await cmd.RunAsync();
        elapsed.TotalMilliseconds.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ConcurrentSetResultAndSetCanceled_DoesNotThrow()
    {
        // Run multiple times to increase chance of hitting the race
        for (var i = 0; i < 100; i++)
        {
            var cmd = new PingCommand(pool: null);
            cmd.Start();

            var t1 = Task.Run(() => cmd.SetResult());
            var t2 = Task.Run(() => cmd.SetCanceled());
            await Task.WhenAll(t1, t2);

            // Should not throw - either result or cancellation wins
            try
            {
                await cmd.RunAsync();
            }
            catch (OperationCanceledException)
            {
                // This is also acceptable
            }
        }
    }
}
