namespace NATS.Client.Core.Commands;

public class PingCommand
{
    public DateTimeOffset WriteTime { get; } = DateTimeOffset.UtcNow;

    public TaskCompletionSource<TimeSpan> TaskCompletionSource { get; } = new();
}
