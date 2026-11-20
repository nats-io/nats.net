using NATS.Client.Core;
#if NETSTANDARD
using Random = NATS.Client.Core.Internal.NetStandardExtensions.Random;
#endif

namespace NATS.Client.JetStream.Internal;

internal static class NatsJSOptsDefaults
{
    private static readonly TimeSpan ExpiresDefault = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpiresMin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatCap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatMin = TimeSpan.FromSeconds(.5);
    private static readonly TimeSpan NoRespondersQuickRepullDelayMin = TimeSpan.FromMilliseconds(10);

    internal static bool TryGetNoRespondersQuickRepullDelay(int quickRepull, TimeSpan idleHeartbeat, out TimeSpan delay)
    {
        // Without heartbeats, the heartbeat timer fires straight after every reply anyway
        if (quickRepull is < 1 or > 8 || idleHeartbeat <= TimeSpan.Zero)
        {
            delay = default;
            return false;
        }

        // Within the idle heartbeat, so the heartbeat timeout (twice that, from the 503) can't fire before the re-pull
        var ticks = Math.Min(NoRespondersQuickRepullDelayMin.Ticks << (quickRepull - 1), idleHeartbeat.Ticks);

        delay = TimeSpan.FromTicks((ticks / 2) + (long)(ticks / 2 * Random.Shared.NextDouble()));
        return true;
    }

    internal static (long MaxMsgs, long MaxBytes, long ThresholdMsgs, long ThresholdBytes) SetMax(
        long? maxMsgs = default,
        long? maxBytes = default,
        long? thresholdMsgs = default,
        long? thresholdBytes = default)
    {
        long maxMsgsOut;
        long maxBytesOut;

        if (maxMsgs.HasValue && maxBytes.HasValue)
        {
            throw new NatsJSException($"You can only set {nameof(maxBytes)} or {nameof(maxMsgs)}");
        }
        else if (!maxMsgs.HasValue && !maxBytes.HasValue)
        {
            throw new NatsJSException($"You must set {nameof(maxBytes)} or {nameof(maxMsgs)}");
        }
        else if (maxMsgs.HasValue && !maxBytes.HasValue)
        {
            maxMsgsOut = maxMsgs.Value;
            maxBytesOut = 0;
        }
        else if (!maxMsgs.HasValue && maxBytes.HasValue)
        {
            maxMsgsOut = 1_000_000;
            maxBytesOut = maxBytes.Value;
        }
        else
        {
            throw new NatsJSException($"Invalid state: {nameof(NatsJSOptsDefaults)}: {nameof(SetMax)}");
        }

        var thresholdMsgsOut = thresholdMsgs ?? maxMsgsOut / 2;

        if (thresholdMsgsOut > maxMsgsOut)
        {
            throw new NatsJSException($"{nameof(thresholdMsgs)} must be less than {nameof(maxMsgs)}");
        }

        var thresholdBytesOut = thresholdBytes ?? maxBytesOut / 2;

        if (thresholdBytesOut > maxBytesOut)
        {
            throw new NatsJSException($"{nameof(thresholdBytes)} must be less than {nameof(maxBytes)}");
        }

        return (maxMsgsOut, maxBytesOut, thresholdMsgsOut, thresholdBytesOut);
    }

    internal static (TimeSpan Expires, TimeSpan IdleHeartbeat) SetTimeouts(
        TimeSpan? expires = default,
        TimeSpan? idleHeartbeat = default)
    {
        var expiresOut = expires ?? ExpiresDefault;

        if (expiresOut < ExpiresMin)
        {
            throw new NatsJSException($"{nameof(expires)} must be greater than {ExpiresMin}");
        }

        var idleHeartbeatOut = idleHeartbeat ?? new TimeSpan(expiresOut.Ticks / 2);

        if (idleHeartbeatOut > HeartbeatCap)
        {
            throw new NatsJSException($"{nameof(idleHeartbeat)} must be less than {HeartbeatCap}");
        }

        if (idleHeartbeatOut != TimeSpan.Zero && idleHeartbeatOut < HeartbeatMin)
        {
            throw new NatsJSException($"{nameof(idleHeartbeat)} must be greater than {HeartbeatMin}");
        }

        return (expiresOut, idleHeartbeatOut);
    }
}
