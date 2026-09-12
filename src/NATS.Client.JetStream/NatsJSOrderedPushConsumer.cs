using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Internal;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// NATS JetStream ordered push consumer.
/// </summary>
public class NatsJSOrderedPushConsumer : INatsJSPushConsumer
{
    private readonly ILogger<NatsJSOrderedPushConsumer> _logger;
    private readonly string _stream;
    private readonly NatsJSContext _context;
    private readonly NatsJSOrderedConsumerOpts _opts;
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Creates a new NATS JetStream ordered push consumer.
    /// </summary>
    /// <param name="stream">Name of the stream.</param>
    /// <param name="context">NATS JetStream context.</param>
    /// <param name="opts">Consumer options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel consume operations.</param>
    public NatsJSOrderedPushConsumer(string stream, NatsJSContext context, NatsJSOrderedConsumerOpts? opts, CancellationToken cancellationToken)
    {
        _logger = context.Connection.Opts.LoggerFactory.CreateLogger<NatsJSOrderedPushConsumer>();
        _stream = stream;
        _context = context;
        _opts = opts ?? NatsJSOrderedConsumerOpts.Default;
        _cancellationToken = cancellationToken;

        // For ordered consumer we start with an empty consumer info object
        // since consumers are created and updated during fetch and consume.
        Info = new ConsumerInfo()
        {
            Name = string.Empty,
            StreamName = string.Empty,
        };
    }

    /// <summary>
    /// Consumer info object created during consume operations.
    /// </summary>
    public ConsumerInfo Info { get; private set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<INatsJSMsg<T>> ConsumeAsync<T>(
        INatsDeserialize<T>? serializer = default,
        NatsJSConsumeOpts? opts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        serializer ??= _context.Connection.Opts.SerializerRegistry.GetDeserializer<T>();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);
        cancellationToken = linkedCts.Token;

        var retry = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_context.Connection is NatsConnection { IsDisposed: true })
                yield break;

            var internalOpts = new NatsJSOrderedPushConsumerOpts
            {
                DeliverPolicy = _opts.DeliverPolicy,
                OptStartSeq = _opts.OptStartSeq,
                OptStartTime = _opts.OptStartTime,
                ReplayPolicy = _opts.ReplayPolicy,
                HeadersOnly = _opts.HeadersOnly,
                FilterSubjects = _opts.FilterSubjects is { Length: > 0 } ? _opts.FilterSubjects : null,
                InactiveThreshold = _opts.InactiveThreshold,
            };

            NatsJSOrderedPushConsumer<T>? pushConsumer = null;
            ChannelReader<NatsJSMsg<T>> reader = default!;
            var errored = false;

            try
            {
                pushConsumer = new NatsJSOrderedPushConsumer<T>(
                    _context, _stream, string.Empty, serializer, internalOpts, new NatsSubOpts(), cancellationToken);
                pushConsumer.Init();
                reader = pushConsumer.Msgs;
            }
            catch (NatsJSProtocolException)
            {
                errored = true;
                if (pushConsumer != null)
                    await pushConsumer.DisposeAsync();
            }

            if (!errored)
            {
                try
                {
                    while (true)
                    {
                        try
                        {
                            var read = await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                            if (!read)
                                break;
                        }
                        catch (OperationCanceledException)
                        {
                            yield break;
                        }
                        catch (NatsJSProtocolException)
                        {
                            errored = true;
                            break;
                        }

                        while (reader.TryRead(out var msg))
                        {
                            yield return msg;
                        }
                    }
                }
                finally
                {
                    if (pushConsumer != null)
                        await pushConsumer.DisposeAsync();
                }
            }

            if (errored)
            {
                await _context.Connection.Opts.BackoffWithJitterAsync(retry++, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<INatsJSMsg<T>?> NextAsync<T>(
        INatsDeserialize<T>? serializer = default,
        NatsJSNextOpts? opts = default,
        CancellationToken cancellationToken = default)
    {
        throw NatsJSProtocolException.ConsumerIsPushBased();
    }

    /// <inheritdoc />
    public IAsyncEnumerable<INatsJSMsg<T>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        CancellationToken cancellationToken = default)
    {
        throw NatsJSProtocolException.ConsumerIsPushBased();
    }

    /// <inheritdoc />
    IAsyncEnumerable<INatsJSMsg<T>> INatsJSConsumer.FetchNoWaitAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer,
        CancellationToken cancellationToken)
    {
        throw NatsJSProtocolException.ConsumerIsPushBased();
    }

    /// <summary>
    /// For ordered consumer this is a no-op.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// For ordered consumer this is a no-op since ordered consumers use ephemeral consumers.
    /// </summary>
    /// <param name="group">The priority group name to unpin.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    public ValueTask UnpinAsync(string group, CancellationToken cancellationToken = default) => default;
}
