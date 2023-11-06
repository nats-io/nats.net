using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream.Internal;

internal static class NatsJSJsonSerializer<T>
{
    private static readonly NatsJsonContextSerializer<T> Default = new(NatsJSJsonSerializerContext.Default);

#pragma warning disable SA1202
    public static readonly INatsSerializer2<T> DefaultSerializer = Default;

    public static readonly INatsDeserializer<T> DefaultDeserializer = Default;
#pragma warning restore SA1202
}

[JsonSerializable(typeof(AccountInfoResponse))]
[JsonSerializable(typeof(AccountLimits))]
[JsonSerializable(typeof(AccountPurgeResponse))]
[JsonSerializable(typeof(AccountStats))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(ApiStats))]
[JsonSerializable(typeof(ClusterInfo))]
[JsonSerializable(typeof(ConsumerConfiguration))]
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
[JsonSerializable(typeof(StreamConfiguration))]
[JsonSerializable(typeof(StreamCreateRequest))]
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
[JsonSerializable(typeof(StreamTemplateConfiguration))]
[JsonSerializable(typeof(StreamTemplateCreateRequest))]
[JsonSerializable(typeof(StreamTemplateCreateResponse))]
[JsonSerializable(typeof(StreamTemplateDeleteResponse))]
[JsonSerializable(typeof(StreamTemplateInfo))]
[JsonSerializable(typeof(StreamTemplateInfoResponse))]
[JsonSerializable(typeof(StreamTemplateNamesRequest))]
[JsonSerializable(typeof(StreamTemplateNamesResponse))]
[JsonSerializable(typeof(StreamUpdateRequest))]
[JsonSerializable(typeof(StreamUpdateResponse))]
[JsonSerializable(typeof(SubjectTransform))]
[JsonSerializable(typeof(Tier))]
internal partial class NatsJSJsonSerializerContext : JsonSerializerContext
{
}
