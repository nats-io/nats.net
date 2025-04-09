using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NATS.Client.Core.Tests;

#if NETFRAMEWORK

internal static class TaskExtensionsCommon
{
    internal static Task WaitAsync(this Task task, CancellationToken cancellationToken)
        => WaitAsync(task, Timeout.InfiniteTimeSpan, cancellationToken);

    internal static Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var timeoutTask = Task.Delay(timeout, cancellationToken);

#pragma warning disable VSTHRD105
        return Task.WhenAny(task, timeoutTask).ContinueWith(
#pragma warning restore VSTHRD105
            // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
            completedTask =>
            {
#pragma warning disable VSTHRD103
                if (completedTask.Result == timeoutTask)
#pragma warning restore VSTHRD103
                {
                    throw new TimeoutException("The operation has timed out.");
                }

                return task;
            },
            cancellationToken).Unwrap();
    }
}

#pragma warning disable SA1201
internal readonly struct VoidResult
#pragma warning restore SA1201
{
}

internal sealed class TaskCompletionSource : TaskCompletionSource<VoidResult>
{
    public TaskCompletionSource(TaskCreationOptions creationOptions = TaskCreationOptions.None)
        : base(creationOptions)
    {
    }

    public new Task Task => base.Task;

    public bool TrySetResult() => TrySetResult(default);

    public void SetResult() => SetResult(default);
}

#pragma warning disable SA1204
internal static class ChannelReaderExtensions
#pragma warning restore SA1204
{
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(this ChannelReader<T> reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var msg))
            {
                yield return msg;
            }
        }
    }
}

#endif
