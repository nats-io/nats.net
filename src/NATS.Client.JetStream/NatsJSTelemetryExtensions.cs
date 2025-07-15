using System.Diagnostics;
using System.Runtime.CompilerServices;
using NATS.Client.Core.Internal;

namespace NATS.Client.JetStream;

public static class NatsJSTelemetryExtensions
{
    /// <summary>Start an activity under the NatsJSMsg associated activity.</summary>
    /// <param name="msg">Nats message</param>
    /// <param name="name">Name of new activity</param>
    /// <param name="tags">Optional tags to add to the activity</param>
    /// <returns>Returns an <see cref="Activity"/> or null if no listeners.</returns>
    public static Activity? StartActivity<T>(
        this in NatsJSMsg<T> msg,
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

    internal static ActivityContext GetActivityContext<T>(this in NatsJSMsg<T> msg) => msg.Headers?.Activity?.Context ?? default;
}
