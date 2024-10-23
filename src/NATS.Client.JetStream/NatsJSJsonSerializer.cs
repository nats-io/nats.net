using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public static class NatsJSJsonSerializer<T>
{
    public static readonly INatsSerializer<T> Default = new NatsJsonContextSerializer<T>(NatsJSJsonSerializerContext.DefaultContext);
}

[JsonSerializable(typeof(AccountInfoResponse))]
[JsonSerializable(typeof(AccountLimits))]
[JsonSerializable(typeof(AccountPurgeResponse))]
[JsonSerializable(typeof(AccountStats))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(ApiStats))]
[JsonSerializable(typeof(ClusterInfo))]
[JsonSerializable(typeof(ConsumerConfig))]
[JsonSerializable(typeof(ConsumerCreateRequest))]
[JsonSerializable(typeof(ConsumerCreateResponse))]
[JsonSerializable(typeof(ConsumerDeleteResponse))]
[JsonSerializable(typeof(ConsumerGetnextRequest))]
[JsonSerializable(typeof(ConsumerInfo))]
[JsonSerializable(typeof(ConsumerInfoResponse))]
[JsonSerializable(typeof(ConsumerLeaderStepdownResponse))]
[JsonSerializable(typeof(ConsumerListRequest))]
[JsonSerializable(typeof(ConsumerListResponse))]
[JsonSerializable(typeof(ConsumerNamesRequest))]
[JsonSerializable(typeof(ConsumerNamesResponse))]
[JsonSerializable(typeof(ConsumerPauseRequest))]
[JsonSerializable(typeof(ConsumerPauseResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ExternalStreamSource))]
[JsonSerializable(typeof(IterableRequest))]
[JsonSerializable(typeof(IterableResponse))]
[JsonSerializable(typeof(LostStreamData))]
[JsonSerializable(typeof(MetaLeaderStepdownRequest))]
[JsonSerializable(typeof(MetaLeaderStepdownResponse))]
[JsonSerializable(typeof(MetaServerRemoveRequest))]
[JsonSerializable(typeof(MetaServerRemoveResponse))]
[JsonSerializable(typeof(PeerInfo))]
[JsonSerializable(typeof(Placement))]
[JsonSerializable(typeof(PubAckResponse))]
[JsonSerializable(typeof(Republish))]
[JsonSerializable(typeof(SequenceInfo))]
[JsonSerializable(typeof(SequencePair))]
[JsonSerializable(typeof(StoredMessage))]
[JsonSerializable(typeof(StreamAlternate))]
[JsonSerializable(typeof(StreamConfig))]
[JsonSerializable(typeof(StreamCreateResponse))]
[JsonSerializable(typeof(StreamDeleteResponse))]
[JsonSerializable(typeof(StreamInfo))]
[JsonSerializable(typeof(StreamInfoRequest))]
[JsonSerializable(typeof(StreamInfoResponse))]
[JsonSerializable(typeof(StreamLeaderStepdownResponse))]
[JsonSerializable(typeof(StreamListRequest))]
[JsonSerializable(typeof(StreamListResponse))]
[JsonSerializable(typeof(StreamMsgDeleteRequest))]
[JsonSerializable(typeof(StreamMsgDeleteResponse))]
[JsonSerializable(typeof(StreamMsgGetRequest))]
[JsonSerializable(typeof(StreamMsgGetResponse))]
[JsonSerializable(typeof(StreamNamesRequest))]
[JsonSerializable(typeof(StreamNamesResponse))]
[JsonSerializable(typeof(StreamPurgeRequest))]
[JsonSerializable(typeof(StreamPurgeResponse))]
[JsonSerializable(typeof(StreamRemovePeerRequest))]
[JsonSerializable(typeof(StreamRemovePeerResponse))]
[JsonSerializable(typeof(StreamRestoreRequest))]
[JsonSerializable(typeof(StreamRestoreResponse))]
[JsonSerializable(typeof(StreamSnapshotRequest))]
[JsonSerializable(typeof(StreamSnapshotResponse))]
[JsonSerializable(typeof(StreamSource))]
[JsonSerializable(typeof(StreamSourceInfo))]
[JsonSerializable(typeof(StreamState))]
[JsonSerializable(typeof(StreamTemplateConfig))]
[JsonSerializable(typeof(StreamTemplateCreateRequest))]
[JsonSerializable(typeof(StreamTemplateCreateResponse))]
[JsonSerializable(typeof(StreamTemplateDeleteResponse))]
[JsonSerializable(typeof(StreamTemplateInfo))]
[JsonSerializable(typeof(StreamTemplateInfoResponse))]
[JsonSerializable(typeof(StreamTemplateNamesRequest))]
[JsonSerializable(typeof(StreamTemplateNamesResponse))]
[JsonSerializable(typeof(StreamUpdateResponse))]
[JsonSerializable(typeof(SubjectTransform))]
[JsonSerializable(typeof(Tier))]
internal partial class NatsJSJsonSerializerContext : JsonSerializerContext
{
#if NET6_0
    internal static readonly NatsJSJsonSerializerContext DefaultContext = new NatsJSJsonSerializerContext(new JsonSerializerOptions());
#else
    internal static readonly NatsJSJsonSerializerContext DefaultContext = new NatsJSJsonSerializerContext(new JsonSerializerOptions
    {
        Converters =
        {
            new JsonStringEnumConverter<ConsumerConfigDeliverPolicy>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<ConsumerConfigAckPolicy>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<ConsumerConfigReplayPolicy>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<StreamConfigCompression>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<StreamConfigDiscard>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<StreamConfigRetention>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<StreamConfigStorage>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<ConsumerCreateAction>(JsonNamingPolicy.SnakeCaseLower),
        },
    });
#endif
}

#if NET6_0
internal class NatsJSJsonStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new InvalidOperationException();
        }

        var stringValue = reader.GetString();

        if (typeToConvert == typeof(ConsumerConfigDeliverPolicy))
        {
            switch (stringValue)
            {
            case "all":
                return (TEnum)(object)ConsumerConfigDeliverPolicy.All;
            case "last":
                return (TEnum)(object)ConsumerConfigDeliverPolicy.Last;
            case "new":
                return (TEnum)(object)ConsumerConfigDeliverPolicy.New;
            case "by_start_sequence":
                return (TEnum)(object)ConsumerConfigDeliverPolicy.ByStartSequence;
            case "by_start_time":
                return (TEnum)(object)ConsumerConfigDeliverPolicy.ByStartTime;
            case "last_per_subject":
                return (TEnum)(object)ConsumerConfigDeliverPolicy.LastPerSubject;
            }
        }

        if (typeToConvert == typeof(ConsumerConfigAckPolicy))
        {
            switch (stringValue)
            {
            case "none":
                return (TEnum)(object)ConsumerConfigAckPolicy.None;
            case "all":
                return (TEnum)(object)ConsumerConfigAckPolicy.All;
            case "explicit":
                return (TEnum)(object)ConsumerConfigAckPolicy.Explicit;
            }
        }

        if (typeToConvert == typeof(ConsumerConfigReplayPolicy))
        {
            switch (stringValue)
            {
            case "instant":
                return (TEnum)(object)ConsumerConfigReplayPolicy.Instant;
            case "original":
                return (TEnum)(object)ConsumerConfigReplayPolicy.Original;
            }
        }

        if (typeToConvert == typeof(StreamConfigCompression))
        {
            switch (stringValue)
            {
            case "none":
                return (TEnum)(object)StreamConfigCompression.None;
            case "s2":
                return (TEnum)(object)StreamConfigCompression.S2;
            }
        }

        if (typeToConvert == typeof(StreamConfigDiscard))
        {
            switch (stringValue)
            {
            case "old":
                return (TEnum)(object)StreamConfigDiscard.Old;
            case "new":
                return (TEnum)(object)StreamConfigDiscard.New;
            }
        }

        if (typeToConvert == typeof(StreamConfigRetention))
        {
            switch (stringValue)
            {
            case "limits":
                return (TEnum)(object)StreamConfigRetention.Limits;
            case "interest":
                return (TEnum)(object)StreamConfigRetention.Interest;
            case "workqueue":
                return (TEnum)(object)StreamConfigRetention.Workqueue;
            }
        }

        if (typeToConvert == typeof(StreamConfigStorage))
        {
            switch (stringValue)
            {
            case "file":
                return (TEnum)(object)StreamConfigStorage.File;
            case "memory":
                return (TEnum)(object)StreamConfigStorage.Memory;
            }
        }

        if (typeToConvert == typeof(ConsumerCreateAction))
        {
            switch (stringValue)
            {
            case "create":
                return (TEnum)(object)ConsumerCreateAction.Create;
            case "update":
                return (TEnum)(object)ConsumerCreateAction.Update;
            default:
                return (TEnum)(object)ConsumerCreateAction.CreateOrUpdate;
            }
        }

        throw new InvalidOperationException($"Reading unknown enum type {typeToConvert.Name} or value {stringValue}");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (value is ConsumerConfigDeliverPolicy consumerConfigDeliverPolicy)
        {
            switch (consumerConfigDeliverPolicy)
            {
            case ConsumerConfigDeliverPolicy.All:
                writer.WriteStringValue("all");
                return;
            case ConsumerConfigDeliverPolicy.Last:
                writer.WriteStringValue("last");
                return;
            case ConsumerConfigDeliverPolicy.New:
                writer.WriteStringValue("new");
                return;
            case ConsumerConfigDeliverPolicy.ByStartSequence:
                writer.WriteStringValue("by_start_sequence");
                return;
            case ConsumerConfigDeliverPolicy.ByStartTime:
                writer.WriteStringValue("by_start_time");
                return;
            case ConsumerConfigDeliverPolicy.LastPerSubject:
                writer.WriteStringValue("last_per_subject");
                return;
            }
        }
        else if (value is ConsumerConfigAckPolicy consumerConfigAckPolicy)
        {
            switch (consumerConfigAckPolicy)
            {
            case ConsumerConfigAckPolicy.None:
                writer.WriteStringValue("none");
                return;
            case ConsumerConfigAckPolicy.All:
                writer.WriteStringValue("all");
                return;
            case ConsumerConfigAckPolicy.Explicit:
                writer.WriteStringValue("explicit");
                return;
            }
        }
        else if (value is ConsumerConfigReplayPolicy consumerConfigReplayPolicy)
        {
            switch (consumerConfigReplayPolicy)
            {
            case ConsumerConfigReplayPolicy.Instant:
                writer.WriteStringValue("instant");
                return;
            case ConsumerConfigReplayPolicy.Original:
                writer.WriteStringValue("original");
                return;
            }
        }
        else if (value is StreamConfigCompression streamConfigCompression)
        {
            switch (streamConfigCompression)
            {
            case StreamConfigCompression.None:
                writer.WriteStringValue("none");
                return;
            case StreamConfigCompression.S2:
                writer.WriteStringValue("s2");
                return;
            }
        }
        else if (value is StreamConfigDiscard streamConfigDiscard)
        {
            switch (streamConfigDiscard)
            {
            case StreamConfigDiscard.Old:
                writer.WriteStringValue("old");
                return;
            case StreamConfigDiscard.New:
                writer.WriteStringValue("new");
                return;
            }
        }
        else if (value is StreamConfigRetention streamConfigRetention)
        {
            switch (streamConfigRetention)
            {
            case StreamConfigRetention.Limits:
                writer.WriteStringValue("limits");
                return;
            case StreamConfigRetention.Interest:
                writer.WriteStringValue("interest");
                return;
            case StreamConfigRetention.Workqueue:
                writer.WriteStringValue("workqueue");
                return;
            }
        }
        else if (value is StreamConfigStorage streamConfigStorage)
        {
            switch (streamConfigStorage)
            {
            case StreamConfigStorage.File:
                writer.WriteStringValue("file");
                return;
            case StreamConfigStorage.Memory:
                writer.WriteStringValue("memory");
                return;
            }
        }
        else if (value is ConsumerCreateAction consumerCreateRequestAction)
        {
            switch (consumerCreateRequestAction)
            {
            case ConsumerCreateAction.CreateOrUpdate:
                // ignore default value
                return;
            case ConsumerCreateAction.Create:
                writer.WriteStringValue("create");
                return;
            case ConsumerCreateAction.Update:
                writer.WriteStringValue("update");
                return;
            }
        }

        throw new InvalidOperationException($"Writing unknown enum value {value.GetType().Name}.{value}");
    }
}
#endif

internal class NatsJSJsonNanosecondsConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new InvalidOperationException("Expected number");
        }

        var value = reader.GetInt64();

        return TimeSpan.FromMilliseconds(value / 1_000_000.0);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((long)(value.TotalMilliseconds * 1_000_000L));
}

internal class NatsJSJsonNullableNanosecondsConverter : JsonConverter<TimeSpan?>
{
    private readonly NatsJSJsonNanosecondsConverter _converter = new();

    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return _converter.Read(ref reader, typeToConvert, options);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            _converter.Write(writer, value.Value, options);
        }
    }
}

internal class NatsJSJsonDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        return reader.GetDateTimeOffset();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        // Write the date time in the format e.g. "2024-01-01T00:00:00Z"
        // instead of the default DateTimeOffset format e.g. "2024-01-01T00:00:00+00:00"
        // which is confusing the server.
        writer.WriteStringValue(value.UtcDateTime);
    }
}
