namespace NATS.Client.Core.Internal;

internal static class TaskCompletionSourceExtensions
{
    /// <summary>
    /// Sets an exception on the task completion source and immediately observes it to avoid unobserved task exceptions surfacing.
    /// </summary>
    /// <param name="source">The task completion source.</param>
    /// <param name="ex">The exception to set and observe.</param>
    /// <returns>True if the exception was successfully set, otherwise false</returns>
    internal static bool TrySetObservedException(this TaskCompletionSource source, Exception ex)
    {
        var result = source.TrySetException(ex);

        if (result)
        {
            _ = source.Task.Exception;
        }

        return result;
    }
}
