namespace NATS.Client.JetStream;

/// <summary>
/// Additional metadata about the message.
/// </summary>
/// <param name="Sequence">
/// The sequence pair for the message.
/// </param>
/// <param name="NumDelivered">
/// The number of times the message was delivered.
/// </param>
/// <param name="NumPending">
/// The number of messages pending for the consumer.
/// </param>
/// <param name="Timestamp">
/// The timestamp of the message.
/// </param>
/// <param name="Stream">
/// The stream the message was sent to.
/// </param>
/// <param name="Consumer">
/// The consumer the message was sent to.
/// </param>
/// <param name="Domain">
/// The domain the message was sent to.
/// </param>
public readonly record struct NatsJSMsgMetadata(NatsJSSequencePair Sequence, ulong NumDelivered, ulong NumPending, DateTimeOffset Timestamp, string Stream, string Consumer, string Domain);

/// <summary>
/// The sequence pair for the message.
/// </summary>
/// <param name="Stream">
/// The stream sequence number.
/// </param>
/// <param name="Consumer">
/// The consumer sequence number.
/// </param>
public readonly record struct NatsJSSequencePair(ulong Stream, ulong Consumer);
