using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace NATS.Client.JetStream;

/// <summary>
/// NATS JetStream ordered consumer.
/// </summary>
public class NatsJSOrderedConsumer : INatsJSConsumer
{
    private readonly ILogger<NatsJSOrderedConsumer> _logger;
    private readonly string _stream;
    private readonly NatsJSContext _context;
    private readonly NatsJSOrderedConsumerOpts _opts;
    private readonly CancellationToken _cancellationToken;
    private ulong _fetchSeq;
    private string _fetchConsumerName = string.Empty;

    /// <summary>
    /// Creates a new NATS JetStream ordered consumer.
    /// </summary>
    /// <param name="stream">Name of the stream.</param>
    /// <param name="context">NATS JetStream context.</param>
    /// <param name="opts">Consumer options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel consume and fetch operations.</param>
    public NatsJSOrderedConsumer(string stream, NatsJSContext context, NatsJSOrderedConsumerOpts opts, CancellationToken cancellationToken)
    {
        _logger = context.Connection.Opts.LoggerFactory.CreateLogger<NatsJSOrderedConsumer>();
        _stream = stream;
        _context = context;
        _opts = opts;
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
    /// Consumer info object created during consume and fetch operations.
    /// </summary>
    public ConsumerInfo Info { get; private set; }

    /// <summary>
    /// Consume messages from the stream in order.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Consume options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel consume operation.</param>
    /// <typeparam name="T">Serialized message data type.</typeparam>
    /// <returns>Asynchronous enumeration which can be used in a <c>await foreach</c> loop.</returns>
    /// <exception cref="NatsJSProtocolException">There was a JetStream server error.</exception>
    public async IAsyncEnumerable<NatsJSMsg<T>> ConsumeAsync<T>(
        INatsDeserialize<T>? serializer = default,
        NatsJSConsumeOpts? opts = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= _context.Opts.DefaultConsumeOpts;
        var consumerName = string.Empty;
        var notificationHandler = opts.NotificationHandler;

        try
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken).Token;

            ulong seq = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var consumer = await RecreateConsumer(consumerName, seq, cancellationToken);
                consumerName = consumer.Info.Name;
                _logger.LogInformation(NatsJSLogEvents.NewConsumer, "Created {ConsumerName} with sequence {Seq}", consumerName, seq);

                NatsJSProtocolException? protocolException = default;

                await using (var cc = await consumer.OrderedConsumeInternalAsync(serializer, opts, cancellationToken))
                {
                    // Keep subscription alive (since it's a wek ref in subscription manager) until we're done.
                    using var anchor = _context.Connection.RegisterSubAnchor(cc);

                    while (true)
                    {
                        // We have to check every call to WaitToReadAsync and TryRead for
                        // protocol exceptions individually because we can't yield return
                        // within try-catch.
                        try
                        {
                            var read = await cc.Msgs.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                            if (!read)
                                break;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (NatsJSProtocolException pe)
                        {
                            protocolException = pe;
                            goto CONSUME_LOOP;
                        }
                        catch (NatsJSConnectionException e)
                        {
                            _logger.LogWarning(NatsJSLogEvents.Retry, "{Error}. Retrying...", e.Message);
                            goto CONSUME_LOOP;
                        }
                        catch (NatsJSTimeoutException e)
                        {
                            notificationHandler?.Invoke(new NatsJSTimeoutNotification(), cancellationToken);
                            _logger.LogWarning(NatsJSLogEvents.Retry, "{Error}. Retrying...", e.Message);
                            goto CONSUME_LOOP;
                        }

                        while (true)
                        {
                            NatsJSMsg<T> msg;

                            var canRead = cc.Msgs.TryRead(out msg);
                            if (!canRead)
                                break;

                            if (msg.Metadata is not { } metadata)
                                continue;

                            seq = metadata.Sequence.Stream;

                            yield return msg;
                        }
                    }
                }

            CONSUME_LOOP:

                _logger.LogWarning(NatsJSLogEvents.Internal, "Consumer loop exited");

                if (protocolException != null)
                {
                    if (protocolException
                        is { HeaderCode: 409, HeaderMessage: NatsHeaders.Messages.ConsumerDeleted }
                        or { HeaderCode: 404 })
                    {
                        // Ignore missing consumer errors and let the
                        // consumer be recreated above.
                    }
                    else
                    {
                        await TryDeleteConsumer(consumerName, cancellationToken);
                        throw protocolException;
                    }
                }

                if (await TryDeleteConsumer(consumerName, cancellationToken))
                {
                    consumerName = string.Empty;
                }
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(consumerName))
                await TryDeleteConsumer(consumerName, cancellationToken);
        }
    }

    /// <summary>
    /// Fetch messages from the stream in order.
    /// </summary>
    /// <param name="opts">Fetch options.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel fetch operation.</param>
    /// <typeparam name="T">Serialized message data type.</typeparam>
    /// <returns>Asynchronous enumeration which can be used in a <c>await foreach</c> loop.</returns>
    public async IAsyncEnumerable<NatsJSMsg<T>> FetchAsync<T>(
        NatsJSFetchOpts opts,
        INatsDeserialize<T>? serializer = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken).Token;

        var consumer = await RecreateConsumer(_fetchConsumerName, _fetchSeq, cancellationToken);
        _fetchConsumerName = consumer.Info.Name;

        await foreach (var msg in consumer.FetchAsync(opts, serializer, cancellationToken))
        {
            if (msg.Metadata is not { } metadata)
                continue;

            _fetchSeq = metadata.Sequence.Stream;
            yield return msg;
        }

        var deleted = await TryDeleteConsumer(_fetchConsumerName, cancellationToken);

        if (deleted)
            _fetchConsumerName = string.Empty;
    }

    /// <summary>
    /// Get the next message from the stream in order.
    /// </summary>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="opts">Next options.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the underlying fetch operation.</param>
    /// <typeparam name="T">Serialized message data type.</typeparam>
    /// <returns>The next NATS JetStream message in order.</returns>
    public async ValueTask<NatsJSMsg<T>?> NextAsync<T>(INatsDeserialize<T>? serializer = default, NatsJSNextOpts? opts = default, CancellationToken cancellationToken = default)
    {
        opts ??= _context.Opts.DefaultNextOpts;

        var fetchOpts = new NatsJSFetchOpts
        {
            MaxMsgs = 1,
            IdleHeartbeat = opts.IdleHeartbeat,
            Expires = opts.Expires,
            NotificationHandler = opts.NotificationHandler,
        };

        await foreach (var msg in FetchAsync(fetchOpts, serializer, cancellationToken))
        {
            return msg;
        }

        return default;
    }

    /// <summary>
    /// For ordered consumer this is a no-op.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the API call.</param>
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private async Task<NatsJSConsumer> RecreateConsumer(string consumer, ulong seq, CancellationToken cancellationToken)
    {
        var consumerOpts = _opts;

        if (seq > 0)
        {
            consumerOpts = _opts with
            {
                OptStartSeq = seq + 1,
                DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
            };

            if (consumer != string.Empty)
            {
                for (var i = 1; ; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await _context.DeleteConsumerAsync(Telemetry.NatsInternalActivities, _stream, consumer, cancellationToken);
                        break;
                    }
                    catch (NatsJSApiNoResponseException)
                    {
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
        }

        ConsumerInfo info;
        for (var i = 0; ; i++)
        {
            try
            {
                info = await _context.CreateOrderedConsumerInternalAsync(_stream, consumerOpts, cancellationToken);
                break;
            }
            catch (NatsJSApiNoResponseException)
            {
            }

            if (i == _opts.MaxResetAttempts)
            {
                throw new NatsJSException("Maximum number of create attempts reached.");
            }
        }

        Info = info;

        return new NatsJSConsumer(_context, info);
    }

    private async ValueTask<bool> TryDeleteConsumer(string consumerName, CancellationToken cancellationToken)
    {
        try
        {
            return await _context.DeleteConsumerAsync(Telemetry.NatsInternalActivities, _stream, consumerName, cancellationToken);
        }
        catch
        {
            return false;
        }
    }
}
