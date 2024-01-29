using System.Diagnostics;
using System.Runtime.CompilerServices;
using NATS.Client.Core;

namespace NATS.Client.Services;

public static class NatsSvcMsgTelemetryExtensions
{
    /// <summary>
    /// Get the activity context associated with a NatsMsg.
    /// </summary>
    /// <returns>
    /// Returns the <see cref="ActivityContext"/> from the 'receive' <see cref="Activity"/> on <see cref="NatsMsg{T}"/>.
    /// The value will be default if no activity is present.
    /// </returns>
    public static ActivityContext GetActivityContext<T>(this in NatsSvcMsg<T> msg)
        => msg.Activity?.Context ?? default;

    /// <summary>Start a child activity under the NatsMsg associated activity.</summary>
    /// <param name="msg">Nats message</param>
    /// <param name="name">Name of new activity</param>
    /// <param name="tags">Optional tags to add to the activity</param>
    /// <returns>Returns child <see cref="Activity"/> or null if no listeners.</returns>
    /// <remarks>
    /// Consider setting Activity.<see cref="Activity.Current"/> to the returned value so that
    ///  the context flows to any child activities created.
    /// </remarks>
    public static Activity? StartChildActivity<T>(
        this in NatsSvcMsg<T> msg,
        [CallerMemberName] string name = "",
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
        => Telemetry.NatsActivities.StartActivity(
            name,
            kind: ActivityKind.Internal,
            parentContext: GetActivityContext(in msg),
            tags: tags);
}
