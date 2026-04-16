namespace NATS.Client.CoreUnit.Tests;

public class NatsConnectionAuthTests
{
    [Fact]
    public async Task ConnectAsync_MissingCredsFile_SecondCallDoesNotHang()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"nats-missing-{Guid.NewGuid():N}.creds");
        var opts = NatsOpts.Default with
        {
            Url = "nats://127.0.0.1:4222",
            RetryOnInitialConnect = false,
            AuthOpts = NatsAuthOpts.Default with { CredsFile = missingPath },
        };

        await using var nats = new NatsConnection(opts);

        var first = async () => await nats.ConnectAsync();
        await first.Should().ThrowAsync<NatsException>();

        var secondTask = nats.ConnectAsync().AsTask();
        var completed = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(secondTask, "second ConnectAsync must not hang");
        var second = async () => await secondTask;
        await second.Should().ThrowAsync<NatsException>();
    }
}
