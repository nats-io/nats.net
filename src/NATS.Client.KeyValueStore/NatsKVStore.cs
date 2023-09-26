using System.Buffers;
using System.Globalization;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace NATS.Client.KeyValueStore;

public enum NatsKVOperation
{
    /// <summary>
    /// A value was put into the bucket
    /// </summary>
    Put,

    /// <summary>
    /// A value was deleted from a bucket
    /// </summary>
    Delete,

    /// <summary>
    /// A value was purged from a bucket
    /// </summary>
    Purge,
}

public class NatsKVStore
{
    private readonly string _bucket;
    private readonly NatsKVOpts _opts;
    private readonly NatsJSContext _context;
    private readonly NatsJSStream _stream;
    private readonly INatsSerializer _serializer;

    internal NatsKVStore(string bucket, NatsKVOpts opts, NatsJSContext context, NatsJSStream stream)
    {
        _bucket = bucket;
        _opts = opts;
        _context = context;
        _stream = stream;
        _serializer = _opts.Serializer ?? _context.Connection.Opts.Serializer;
    }

    public async ValueTask PutAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        // PUB $KV.profiles.sue.color
        var ack = await _context.PublishAsync($"$KV.{_bucket}.{key}", value, cancellationToken: cancellationToken);
        ack.EnsureSuccess();
    }

    public async ValueTask<NatsKVEntry<T?>> GetEntryAsync<T>(string key, INatsSerializer? serializer = default, CancellationToken cancellationToken = default)
    {
        // API--------------+ stream----+ subject--------------+
        // $JS.API.DIRECT.GET.KV_profiles.$KV.profiles.sue.color

        /*
       < PUB $JS.API.DIRECT.GET.KV_profiles.$KV.profiles.sue.color _INBOX.QtAfMtjInNmkBUv64qBTHj 0
       > HMSG _INBOX.QtAfMtjInNmkBUv64qBTHj 0 141 145
         NATS/1.0
         Nats-Stream: KV_profiles
         Nats-Subject: $KV.profiles.sue.color
         Nats-Sequence: 8
         Nats-Time-Stamp: 2023-09-26T08:35:49.3726545Z

         blue
         */
        if (_stream.Info.Config.AllowDirect)
        {
            var direct = await _stream.GetDirectAsync<T>($"$KV.{_bucket}.{key}", serializer ?? _serializer, cancellationToken);
            if (direct is { Headers: { } headers } msg)
            {
                var d = msg.Data;
                var stringValues = headers["x"];

                if (!headers.TryGetValue("Nats-Sequence", out var sequenceValues))
                    throw new NatsKVException("Missing sequence header");

                if (sequenceValues.Count != 1)
                    throw new NatsKVException("Unexpected number of sequence headers");

                if (!long.TryParse(sequenceValues[0], out var sequence))
                    throw new NatsKVException("Can't parse sequence header");

                if (!headers.TryGetValue("Nats-Time-Stamp", out var timestampValues))
                    throw new NatsKVException("Missing timestamp header");

                if (timestampValues.Count != 1)
                    throw new NatsKVException("Unexpected number of timestamp headers");

                if (!DateTimeOffset.TryParse(timestampValues[0], out var timestamp))
                    throw new NatsKVException("Can't parse timestamp header");

                var operation = NatsKVOperation.Put;
                if (headers.TryGetValue("KV-Operation", out var operationValues))
                {
                    if (operationValues.Count != 1)
                        throw new NatsKVException("Unexpected number of operation headers");

                    if (!Enum.TryParse(operationValues[0], ignoreCase: true, out operation))
                        throw new NatsKVException("Can't parse operation header");
                }

                return new NatsKVEntry<T?>(_bucket, key)
                {
                    Bucket = _bucket,
                    Key = key,
                    Created = timestamp,
                    Revision = sequence,
                    Operation = operation,
                    Value = msg.Data,
                    Delta = 0,
                    UsedDirectGet = true,
                };
            }
            else
            {
                throw new NatsKVException("Missing headers");
            }
        }
        else
        {
/*[3] --> PUB $JS.API.STREAM.MSG.GET.KV_profiles _INBOX.thLyHrjut8ccR4XYaOdm3f 41
        {"last_by_subj":"$KV.profiles.sue.color"}

[3] <-- MSG _INBOX.thLyHrjut8ccR4XYaOdm3f 0 170
        {"type":"io.nats.jetstream.api.v1.stream_msg_get_response","message":{
        "subject":"$KV.profiles.sue.color",
        "seq":1,
        "data":"Ymx1ZQ==",
        "time":"2023-09-26T09:18:35.8845678Z"}}*/
            var response = await _stream.GetAsync(new StreamMsgGetRequest { LastBySubj = $"$KV.{_bucket}.{key}" }, cancellationToken);

            if (!DateTimeOffset.TryParse(response.Message.Time, out var created))
                throw new NatsKVException("Can't parse timestamp message value");

            T? data;
            var bytes = ArrayPool<byte>.Shared.Rent(_context.Connection.Opts.ReaderBufferSize);
            try
            {
                if (Convert.TryFromBase64String(response.Message.Data, bytes, out var written))
                {
                    var buffer = new ReadOnlySequence<byte>(bytes.AsMemory(0, written));
                    data = (serializer ?? _serializer).Deserialize<T>(buffer);
                }
                else
                {
                    throw new NatsKVException("Can't decode data message value");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            return new NatsKVEntry<T?>(_bucket, key)
            {
                Created = created,
                Revision = response.Message.Seq,
                Value = data,
                UsedDirectGet = false,
            };
        }
    }
}
