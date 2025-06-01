using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Drawing;

namespace NATS.Client.Core.Internal;

// https://opentelemetry.io/docs/specs/semconv/attributes-registry/messaging/
// https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#messaging-attributes
internal static class Telemetry
{
    internal static readonly ActivitySource NatsActivities = new(name: NatsActivitySource);
    private const string NatsActivitySource = "NATS.Net";
    private const string MeterName = "NATS.Net";
    private static readonly Meter NatsMeter = new Meter(MeterName);

    private static readonly Counter<long> _subscriptionCounter = NatsMeter.CreateCounter<long>(
            Constants.SubscriptionInstrumentName,
            unit: "{subscriptions}",
            description: "Number of subscriptions");

    private static readonly Counter<long> _pendingMessagesCounter = NatsMeter.CreateCounter<long>(
            Constants.PendingMessagesInstrumentName,
            unit: "{messages}",
            description: "Number of pending messages");

    private static readonly Histogram<long> _sentBytesHistogram = NatsMeter.CreateHistogram<long>(
            Constants.SentBytesInstrumentName,
            unit: "{bytes}",
            description: "Number of bytes sent");

    private static readonly Histogram<long> _receivedBytesHistogram = NatsMeter.CreateHistogram<long>(
            Constants.ReceivedBytesInstrumentName,
            unit: "{bytes}",
            description: "Number of bytes received");

    private static readonly Counter<long> _sentMessagesCounter = NatsMeter.CreateCounter<long>(
            Constants.SentMessagesInstrumentName,
            unit: "{messages}",
            description: "Number of messages sent");

    private static readonly Counter<long> _receivedMessagesCounter = NatsMeter.CreateCounter<long>(
            Constants.ReceivedMessagesInstrumentName,
            unit: "{messages}",
            description: "Number of messages received");

    private static readonly Histogram<double> _durationOperationHistogram = NatsMeter.CreateHistogram<double>(
        Constants.DurationOperationInstrumentName,
        unit: "{s}",
        description: "Duration of messaging operation initiated by a producer or consumer client.");

    private static readonly Histogram<double> _durationProcessHistogram = NatsMeter.CreateHistogram<double>(
        Constants.DurationProcessInstrumentName,
        unit: "{s}",
        description: "Duration of processing operation within client.");

    private static readonly object BoxedTrue = true;

    internal static bool HasListeners() => NatsActivities.HasListeners();

    internal static List<KeyValuePair<string, object?>> GetTags(INatsServerInfo? serverInfo, NatsOperationProps props)
    {
        var tags = new List<KeyValuePair<string, object?>>()
        {
            new KeyValuePair<string, object?>(Constants.SystemKey, Constants.SystemVal),

            new KeyValuePair<string, object?>(Constants.DestName, props.Subject),
            new KeyValuePair<string, object?>(Constants.DestTemplate, props.SubjectTemplate),
        };

        if (serverInfo != null)
        {
            tags.Add(new KeyValuePair<string, object?>(Constants.NetworkProtoName, "nats"));
            tags.Add(new KeyValuePair<string, object?>(Constants.NetworkProtoVersion, serverInfo.ProtocolVersion.ToString()));
            tags.Add(new KeyValuePair<string, object?>(Constants.ServerAddress, serverInfo.Host));
            tags.Add(new KeyValuePair<string, object?>(Constants.ServerPort, serverInfo.Port.ToString()));
        }

        return tags;
    }

    internal static Activity? StartActivity(
        DateTimeOffset date,
        NatsOperationProps props,
        INatsServerInfo? serverInfo,
        string operation,
        List<KeyValuePair<string, object?>> tags,
        ActivityContext? parentContext = null)
    {
        if (!NatsActivities.HasListeners())
            return null;
        var destination = props.UsesInbox ? "inbox" : props.SantisedSubject();

        tags.Add(new KeyValuePair<string, object?>(Constants.DestIsTemporary, props.UsesInbox ? Constants.True : Constants.False));
        tags.Add(new KeyValuePair<string, object?>(Constants.DestIsAnonymous, Constants.False));
        tags.Add(new KeyValuePair<string, object?>(Constants.Subject, props.Subject));
        tags.Add(new KeyValuePair<string, object?>(Constants.OpKey, operation));

        if (serverInfo != null)
        {
            tags.Add(new KeyValuePair<string, object?>(Constants.ClientId, serverInfo.ClientId.ToString()));
            tags.Add(new KeyValuePair<string, object?>(Constants.NetworkLocalAddress, serverInfo.ClientIp));
            tags.Add(new KeyValuePair<string, object?>(Constants.NetworkPeerAddress, serverInfo.Host));
            tags.Add(new KeyValuePair<string, object?>(Constants.NetworkPeerPort, serverInfo.Port.ToString()));
        }

        if (props is NatsProcessProps process)
        {
            tags.Add(new KeyValuePair<string, object?>(Constants.DestSubscription, process.SubscriptionId));
            tags.Add(new KeyValuePair<string, object?>(Constants.QueueGroup, process.Subscription?.QueueGroup));
        }

        if (props is NatsPublishProps publish)
        {
            tags.Add(new KeyValuePair<string, object?>(Constants.ReplyToName, publish.ReplyTo));
        }

        var name = $"{destination} {operation}";
        var activity = NatsActivities.StartActivity(
            name,
            kind: ActivityKind.Producer,
            startTime: date,
            parentContext: parentContext ?? default,
            tags: tags);
        return activity;
    }

    internal static void AddTraceContextHeaders(Activity? activity, ref NatsHeaders? headers)
    {
        if (!NatsActivities.HasListeners())
            return;

        headers ??= new NatsHeaders();
        DistributedContextPropagator.Current.Inject(
            activity: activity,
            carrier: headers,
            setter: static (carrier, fieldName, fieldValue) =>
            {
                if (carrier is not NatsHeaders headers)
                {
                    Debug.Assert(false, "This should never be hit.");
                    return;
                }

                // There are cases where headers reused internally (e.g. JetStream publish retry)
                // there may also be cases where application can reuse the same header
                // in which case we should still be able to overwrite headers with telemetry fields
                // even though headers would be set to readonly before being passed down in publish methods.
                headers.SetOverrideReadOnly(fieldName, fieldValue);
            });
    }

    internal static void SetException(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        // see: https://opentelemetry.io/docs/specs/semconv/attributes-registry/exception/
        var message = GetMessage(exception);
        var eventTags = new ActivityTagsCollection
        {
            ["exception.escaped"] = BoxedTrue,
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = message,
            ["exception.stacktrace"] = GetStackTrace(exception),
        };

        var activityEvent = new ActivityEvent("exception", DateTimeOffset.UtcNow, eventTags);

        activity.AddEvent(activityEvent);
        activity.SetStatus(ActivityStatusCode.Error, message);
        return;

        static string GetMessage(Exception exception)
        {
            try
            {
                return exception.Message;
            }
            catch
            {
                return $"An exception of type {exception.GetType()} was thrown but the Message property threw an exception.";
            }
        }

        static string GetStackTrace(Exception? exception)
        {
            var stackTrace = exception?.StackTrace;
#if NETSTANDARD2_0
            return string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace!;
#else
            return string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace;
#endif
        }
    }

    internal static void IncrementSubscriptionCount(List<KeyValuePair<string, object?>> tags)
        => _subscriptionCounter.Add(1, tags.ToArray());

    internal static void DecrementSubscriptionCount(List<KeyValuePair<string, object?>> tags)
        => _subscriptionCounter.Add(-1, tags.ToArray());

    internal static void RecordSentBytes(long bytes, List<KeyValuePair<string, object?>> tags)
        => _sentBytesHistogram.Record(bytes, tags.ToArray());

    internal static void RecordReceivedBytes(long bytes, List<KeyValuePair<string, object?>> tags)
        => _receivedBytesHistogram.Record(bytes, tags.ToArray());

    internal static void AddPendingMessages(long messages, List<KeyValuePair<string, object?>> tags)
        => _pendingMessagesCounter.Add(messages, tags.ToArray());

    internal static void AddSentMessages(long messages, List<KeyValuePair<string, object?>> tags)
        => _sentMessagesCounter.Add(messages, tags.ToArray());

    internal static void AddReceivedMessages(long messages, List<KeyValuePair<string, object?>> tags)
        => _receivedMessagesCounter.Add(messages, tags.ToArray());

    internal static void RecordOperationDuration(string operation, TimeSpan duration, List<KeyValuePair<string, object?>> tags)
    {
        tags.Add(new KeyValuePair<string, object?>(Constants.OpKey, operation));
        _durationOperationHistogram.Record(duration.TotalSeconds, tags.ToArray());
    }

    internal static void RecordProcessDuration(long bytes, KeyValuePair<string, object?>[] tags)
    {
        _durationProcessHistogram.Record(bytes, tags);
    }

    private static bool TryParseTraceContext(NatsHeaders headers, out ActivityContext context)
    {
        DistributedContextPropagator.Current.ExtractTraceIdAndState(
            carrier: headers,
            getter: static (object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues) =>
            {
                if (carrier is not NatsHeaders headers)
                {
                    Debug.Assert(false, "This should never be hit.");
                    fieldValue = null;
                    fieldValues = null;
                    return;
                }

                if (headers.TryGetValue(fieldName, out var values))
                {
                    if (values.Count == 1)
                    {
                        fieldValue = values[0];
                        fieldValues = null;
                    }
                    else
                    {
                        fieldValue = null;
                        fieldValues = values;
                    }
                }
                else
                {
                    fieldValue = null;
                    fieldValues = null;
                }
            },
            out var traceParent,
            out var traceState);

        return ActivityContext.TryParse(traceParent, traceState, out context);
    }

    internal class Constants
    {
        public const string True = "true";
        public const string False = "false";
        public const string RequestReplyActivityName = "request";
        public const string PublishActivityName = "publish";
        public const string SubscribeActivityName = "subscribe";
        public const string UnsubscribeActivityName = "unsubscribe";
        public const string CreateActivityName = "create";
        public const string ReceiveActivityName = "receive";

        public const string SystemKey = "messaging.system";
        public const string SystemVal = "nats";
        public const string ClientId = "messaging.client_id";
        public const string OpKey = "messaging.operation";
        public const string OpPub = "publish";
        public const string OpRec = "receive";
        public const string MsgBodySize = "messaging.message.body.size";
        public const string MsgTotalSize = "messaging.message.envelope.size";

        public const string DestTemplate = "messaging.destination.template";
        public const string DestName = "messaging.destination.name";
        public const string DestSubscription = "messaging.destination.subscription.name";
        public const string DestIsTemporary = "messaging.destination.temporary";
        public const string DestIsAnonymous = "messaging.destination.anonymous";
        public const string DestPubName = "messaging.destination_publish.name";

        public const string QueueGroup = "messaging.consumer.group.name";
        public const string ReplyToName = "messaging.nats.message.reply_to";
        public const string Subject = "messaging.nats.message.subject";

        public const string ServerAddress = "server.address";
        public const string ServerPort = "server.port";
        public const string NetworkProtoName = "network.protocol.name";
        public const string NetworkProtoVersion = "network.protocol.version";
        public const string NetworkPeerAddress = "network.peer.address";
        public const string NetworkPeerPort = "network.peer.port";
        public const string NetworkLocalAddress = "network.local.address";

        public const string PendingMessagesInstrumentName = $"messaging.client.pending.messages";
        public const string SentBytesInstrumentName = $"messaging.client.sent.bytes";
        public const string ReceivedBytesInstrumentName = $"messaging.client.consumed.bytes";
        public const string SentMessagesInstrumentName = $"messaging.client.sent.messages";
        public const string ReceivedMessagesInstrumentName = $"messaging.client.consumed.messages";
        public const string SubscriptionInstrumentName = $"messaging.client.nats.subscription.count";
        public const string DurationOperationInstrumentName = $"messaging.client.operation.duration";
        public const string DurationProcessInstrumentName = $"messaging.process.duration";
    }
}
