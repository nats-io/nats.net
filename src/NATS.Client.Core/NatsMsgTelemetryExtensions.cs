using System.Diagnostics;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.Core;

public static class NatsMsgTelemetryExtensions
{
    /// <summary>Start an activity under the NatsMsg associated activity.</summary>
    /// <param name="msg">Nats message</param>
    /// <param name="name">Name of new activity</param>
    /// <param name="tags">Optional tags to add to the activity</param>
    /// <returns>Returns an <see cref="Activity"/> or null if no listeners.</returns>
    public static Activity? StartActivity<T>(
        this in NatsMsg<T> msg,
        [CallerMemberName] string name = "",
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        if (!Telemetry.HasListeners())
            return null;

        return Telemetry.NatsActivities.StartActivity(
            name,
            kind: ActivityKind.Internal,
            parentContext: GetActivityContext(in msg),
            tags: tags);
    }

    internal static ActivityContext GetActivityContext<T>(this in NatsMsg<T> msg) => msg.Headers?.Activity?.Context ?? default;
}
