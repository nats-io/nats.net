using System.Runtime.CompilerServices;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

public class NatsJSOrderedConsumer
{
    private readonly string _stream;
    private readonly NatsJSContext _context;
    private readonly NatsJSOrderedConsumerOpts _opts;
    private readonly CancellationToken _cancellationToken;
    private ulong _seq;
    private string _consumer = string.Empty;

    public NatsJSOrderedConsumer(string stream, NatsJSContext context, NatsJSOrderedConsumerOpts opts, CancellationToken cancellationToken)
    {
        _stream = stream;
        _context = context;
        _opts = opts;
        _cancellationToken = cancellationToken;
    }

    public async IAsyncEnumerable<NatsJSMsg<T?>> FetchAllAsync<T>(
        NatsJSFetchOpts? opts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken).Token;

        var consumer = await GetConsumer(cancellationToken);

        await foreach (var msg in consumer.FetchAllAsync<T>(opts, cancellationToken))
        {
            if (msg.Metadata is not { } metadata)
                continue;

            _seq = metadata.Sequence.Stream;
            yield return msg;
        }
    }

    private async Task<NatsJSConsumer> GetConsumer(CancellationToken cancellationToken)
    {
        var consumerOpts = _opts;
        if (_consumer != string.Empty)
        {
            consumerOpts = _opts with
            {
                OptStartSeq = _seq + 1,
                DeliverPolicy = ConsumerConfigurationDeliverPolicy.by_start_sequence,
            };

            for (var i = 1; ; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await _context.DeleteConsumerAsync(_stream, _consumer, cancellationToken);
                    break;
                }
                catch (NatsJSApiException apiException)
                {
                    if (apiException.Error.Code == 404)
                    {
                        break;
                    }
                }

                if (i == _opts.MaxResetAttempts)
                {
                    throw new NatsJSException("Maximum number of delete attempts reached.");
                }
            }
        }

        var info = await _context.CreateOrderedConsumerInternalAsync(_stream, consumerOpts, cancellationToken);
        _consumer = info.Config.Name;

        var consumer = new NatsJSConsumer(_context, info);
        return consumer;
    }
}
