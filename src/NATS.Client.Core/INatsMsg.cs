namespace NATS.Client.Core;

/// <summary>
/// Shared contract for all NATS message types.
/// </summary>
public interface INatsMsg
{
    /// <summary>
    /// Headers of the user message if set.
    /// </summary>
    NatsHeaders? Headers { get; }
}
