namespace NATS.Client.JetStream.Internal;

internal static class NatsJSOpsDefaults
{
    private static readonly TimeSpan ExpiresDefault = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExpiresMin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HeartbeatCap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatMin = TimeSpan.FromSeconds(.5);

    internal static (long MaxMsgs, long MaxBytes, long ThresholdMsgs, long ThresholdBytes) SetMax(
        NatsJSOpts? opts = default,
        long? maxMsgs = default,
        long? maxBytes = default,
        long? thresholdMsgs = default,
        long? thresholdBytes = default)
    {
        var jsOpts = opts ?? new NatsJSOpts();
        long maxMsgsOut;
        long maxBytesOut;

        if (maxMsgs.HasValue && maxBytes.HasValue)
        {
            throw new NatsJSException($"You can only set {nameof(maxBytes)} or {nameof(maxMsgs)}");
        }
        else if (!maxMsgs.HasValue && !maxBytes.HasValue)
        {
            maxMsgsOut = jsOpts.MaxMsgs;
            maxBytesOut = 0;
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
            throw new NatsJSException($"Invalid state: {nameof(NatsJSOpsDefaults)}: {nameof(SetMax)}");
        }

        var thresholdMsgsOut = thresholdMsgs ?? maxMsgsOut / 2;
        if (thresholdMsgsOut > maxMsgsOut)
            thresholdMsgsOut = maxMsgsOut;

        var thresholdBytesOut = thresholdBytes ?? maxBytesOut / 2;
        if (thresholdBytesOut > maxBytesOut)
            thresholdBytesOut = maxBytesOut;

        return (maxMsgsOut, maxBytesOut, thresholdMsgsOut, thresholdBytesOut);
    }

    internal static (TimeSpan Expires, TimeSpan IdleHeartbeat) SetTimeouts(
        TimeSpan? expires = default,
        TimeSpan? idleHeartbeat = default)
    {
        var expiresOut = expires ?? ExpiresDefault;
        if (expiresOut < ExpiresMin)
            expiresOut = ExpiresMin;

        var idleHeartbeatOut = idleHeartbeat ?? expiresOut / 2;
        if (idleHeartbeatOut > HeartbeatCap)
            idleHeartbeatOut = HeartbeatCap;
        if (idleHeartbeatOut < HeartbeatMin)
            idleHeartbeatOut = HeartbeatMin;

        return (expiresOut, idleHeartbeatOut);
    }
}
