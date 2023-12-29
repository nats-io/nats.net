namespace NATS.Client.Core.Commands;

internal sealed class PingCommand
{
    public DateTimeOffset WriteTime { get; } = DateTimeOffset.UtcNow;

    public TaskCompletionSource<TimeSpan> TaskCompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
