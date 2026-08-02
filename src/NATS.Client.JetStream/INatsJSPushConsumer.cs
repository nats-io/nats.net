namespace NATS.Client.JetStream;

/// <summary>
/// Represents a NATS JetStream push consumer.
/// </summary>
/// <remarks>
/// <para>
/// A push consumer is a JetStream consumer that delivers messages to a client
/// subscription on a subject (the consumer's delivery subject). The server pushes
/// messages and control messages (heartbeats and flow-control requests) to that
/// subject as long as at least one subscriber is active on it.
/// </para>
/// <para>
/// The NATS server manages delivery to the delivery subject, and messages are
/// obtained with <see cref="INatsJSConsumer.ConsumeAsync{T}"/>. The receipt of
/// heartbeats and flow-control requests is handled automatically by the client.
/// </para>
/// <para>
/// A push consumer does not support pull-based operations such as
/// <see cref="INatsJSConsumer.NextAsync{T}"/>, <see cref="INatsJSConsumer.FetchAsync{T}"/>
/// or <see cref="INatsJSConsumer.FetchNoWaitAsync{T}"/> which are only available for pull
/// (work-queue) consumers and throw <see cref="NatsJSProtocolException"/> when called.
/// </para>
/// </remarks>
public interface INatsJSPushConsumer : INatsJSConsumer;
