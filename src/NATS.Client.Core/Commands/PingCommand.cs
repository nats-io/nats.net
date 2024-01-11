namespace NATS.Client.Core.Commands;

internal struct PingCommand
{
    public PingCommand()
    {
    }

    public DateTimeOffset WriteTime { get; } = DateTimeOffset.UtcNow;

    public TaskCompletionSource<TimeSpan> TaskCompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
