namespace NATS.Client.Core.Internal;

internal static class TimeoutValidation
{
    // Timer.Change and Task.WaitAsync throw for dueTime > (uint.MaxValue - 1) ms (~49.7 days).
    // TimeSpan.MaxValue and Timeout.InfiniteTimeSpan are accepted as explicit "no timeout" sentinels.
    public static readonly TimeSpan MaxSupportedTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public static TimeSpan Validate(TimeSpan? value, string paramName, TimeSpan noTimeoutResult)
    {
        if (value is not { } v || v == TimeSpan.MaxValue || v == Timeout.InfiniteTimeSpan)
            return noTimeoutResult;
        if (v < TimeSpan.Zero || v > MaxSupportedTimeout)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                v,
                $"{paramName} must be between TimeSpan.Zero and {MaxSupportedTimeout}, or TimeSpan.MaxValue / Timeout.InfiniteTimeSpan for no timeout.");
        }

        return v;
    }
}
