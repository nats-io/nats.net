using System.Diagnostics;

namespace NATS.Client.Core.Internal;

// https://opentelemetry.io/docs/specs/semconv/attributes-registry/messaging/
// https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/#messaging-attributes
internal static class Telemetry
{
    internal static readonly ActivitySource NatsActivities = new(name: NatsActivitySource);

    private const string NatsActivitySource = "NATS.Net";
    private static readonly object BoxedTrue = true;

    internal static bool HasListeners() => NatsActivities.HasListeners();

    internal static Activity? StartSendActivity(
        string name,
        INatsConnection? connection,
        string subject,
        string? replyTo,
        ActivityContext? parentContext = null)
    {
        if (!NatsActivities.HasListeners())
            return null;

        KeyValuePair<string, object?>[] tags;
        if (connection is NatsConnection { ServerInfo: not null } conn)
        {
            var len = 11;
            if (replyTo is not null)
                len++;

            var serverPort = conn.ServerInfo.Port.ToString();
            tags = new KeyValuePair<string, object?>[len];
            tags[0] = new KeyValuePair<string, object?>(Constants.SystemKey, Constants.SystemVal);
            tags[1] = new KeyValuePair<string, object?>(Constants.OpKey, Constants.OpPub);
            tags[2] = new KeyValuePair<string, object?>(Constants.DestName, subject);

            tags[3] = new KeyValuePair<string, object?>(Constants.ClientId, conn.ServerInfo.ClientId.ToString());
            tags[4] = new KeyValuePair<string, object?>(Constants.ServerAddress, conn.ServerInfo.Host);
            tags[5] = new KeyValuePair<string, object?>(Constants.ServerPort, serverPort);
            tags[6] = new KeyValuePair<string, object?>(Constants.NetworkProtoName, "nats");
            tags[7] = new KeyValuePair<string, object?>(Constants.NetworkProtoVersion, conn.ServerInfo.ProtocolVersion.ToString());
            tags[8] = new KeyValuePair<string, object?>(Constants.NetworkPeerAddress, conn.ServerInfo.Host);
            tags[9] = new KeyValuePair<string, object?>(Constants.NetworkPeerPort, serverPort);
            tags[10] = new KeyValuePair<string, object?>(Constants.NetworkLocalAddress, conn.ServerInfo.ClientIp);

            if (replyTo is not null)
                tags[11] = new KeyValuePair<string, object?>(Constants.ReplyTo, replyTo);
        }
        else
        {
            var len = 3;
            if (replyTo is not null)
                len++;

            tags = new KeyValuePair<string, object?>[len];
            tags[0] = new KeyValuePair<string, object?>(Constants.SystemKey, Constants.SystemVal);
            tags[1] = new KeyValuePair<string, object?>(Constants.OpKey, Constants.OpPub);
            tags[2] = new KeyValuePair<string, object?>(Constants.DestName, subject);

            if (replyTo is not null)
                tags[3] = new KeyValuePair<string, object?>(Constants.ReplyTo, replyTo);
        }

        return NatsActivities.StartActivity(
            name,
            kind: ActivityKind.Producer,
            parentContext: parentContext ?? default,
            tags: tags);
    }

    internal static void AddTraceContextHeaders(Activity? activity, ref NatsHeaders? headers)
    {
        if (activity is null)
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

                headers[fieldName] = fieldValue;
            });
    }

    internal static Activity? StartReceiveActivity(
        INatsConnection? connection,
        string name,
        string subscriptionSubject,
        string? queueGroup,
        string subject,
        string? replyTo,
        long bodySize,
        long size,
        NatsHeaders? headers)
    {
        if (!NatsActivities.HasListeners())
            return null;

        KeyValuePair<string, object?>[] tags;
        if (connection is NatsConnection { ServerInfo: not null } conn)
        {
            var serverPort = conn.ServerInfo.Port.ToString();

            var len = 17;
            if (replyTo is not null)
                len++;
            if (queueGroup is not null)
                len++;

            tags = new KeyValuePair<string, object?>[len];
            tags[0] = new KeyValuePair<string, object?>(Constants.SystemKey, Constants.SystemVal);
            tags[1] = new KeyValuePair<string, object?>(Constants.OpKey, Constants.OpRec);
            tags[2] = new KeyValuePair<string, object?>(Constants.DestTemplate, subscriptionSubject);
            tags[3] = new KeyValuePair<string, object?>(Constants.DestIsTemporary, subscriptionSubject.StartsWith(conn.InboxPrefix, StringComparison.Ordinal) ? Constants.True : Constants.False);
            tags[4] = new KeyValuePair<string, object?>(Constants.Subject, subject);
            tags[5] = new KeyValuePair<string, object?>(Constants.DestName, subject);
            tags[6] = new KeyValuePair<string, object?>(Constants.DestPubName, subject);
            tags[7] = new KeyValuePair<string, object?>(Constants.MsgBodySize, bodySize.ToString());
            tags[8] = new KeyValuePair<string, object?>(Constants.MsgTotalSize, size.ToString());
            tags[9] = new KeyValuePair<string, object?>(Constants.ClientId, conn.ServerInfo.ClientId.ToString());
            tags[10] = new KeyValuePair<string, object?>(Constants.ServerAddress, conn.ServerInfo.Host);
            tags[11] = new KeyValuePair<string, object?>(Constants.ServerPort, serverPort);
            tags[12] = new KeyValuePair<string, object?>(Constants.NetworkProtoName, "nats");
            tags[13] = new KeyValuePair<string, object?>(Constants.NetworkProtoVersion, conn.ServerInfo.ProtocolVersion.ToString());
            tags[14] = new KeyValuePair<string, object?>(Constants.NetworkPeerAddress, conn.ServerInfo.Host);
            tags[15] = new KeyValuePair<string, object?>(Constants.NetworkPeerPort, serverPort);
            tags[16] = new KeyValuePair<string, object?>(Constants.NetworkLocalAddress, conn.ServerInfo.ClientIp);

            var index = 17;
            if (replyTo is not null)
                tags[index++] = new KeyValuePair<string, object?>(Constants.ReplyTo, replyTo);
            if (queueGroup is not null)
                tags[index] = new KeyValuePair<string, object?>(Constants.QueueGroup, queueGroup);
        }
        else
        {
            tags = new KeyValuePair<string, object?>[10];
            tags[0] = new KeyValuePair<string, object?>(Constants.SystemKey, Constants.SystemVal);
            tags[1] = new KeyValuePair<string, object?>(Constants.OpKey, Constants.OpRec);
            tags[2] = new KeyValuePair<string, object?>(Constants.DestTemplate, subscriptionSubject);
            tags[3] = new KeyValuePair<string, object?>(Constants.QueueGroup, queueGroup);
            tags[4] = new KeyValuePair<string, object?>(Constants.Subject, subject);
            tags[5] = new KeyValuePair<string, object?>(Constants.DestName, subject);
            tags[6] = new KeyValuePair<string, object?>(Constants.DestPubName, subject);
            tags[7] = new KeyValuePair<string, object?>(Constants.MsgBodySize, bodySize.ToString());
            tags[8] = new KeyValuePair<string, object?>(Constants.MsgTotalSize, size.ToString());

            if (replyTo is not null)
                tags[9] = new KeyValuePair<string, object?>(Constants.ReplyTo, replyTo);
        }

        if (headers is null || !TryParseTraceContext(headers, out var context))
            context = default;

        return NatsActivities.StartActivity(
            name,
            kind: ActivityKind.Consumer,
            parentContext: context,
            tags: tags);
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
            return string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace;
        }
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
        public const string DestIsTemporary = "messaging.destination.temporary";
        public const string DestPubName = "messaging.destination_publish.name";

        public const string QueueGroup = "messaging.nats.consumer.group";
        public const string ReplyTo = "messaging.nats.message.reply_to";
        public const string Subject = "messaging.nats.message.subject";

        public const string ServerAddress = "server.address";
        public const string ServerPort = "server.port";
        public const string NetworkProtoName = "network.protocol.name";
        public const string NetworkProtoVersion = "network.protocol.version";
        public const string NetworkPeerAddress = "network.peer.address";
        public const string NetworkPeerPort = "network.peer.port";
        public const string NetworkLocalAddress = "network.local.address";
    }
}
