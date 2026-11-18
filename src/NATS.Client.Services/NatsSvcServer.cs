using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.Services.Internal;
using NATS.Client.Services.Models;
#if NETSTANDARD
using NATS.Client.Core.Internal.NetStandardExtensions;
#endif

namespace NATS.Client.Services;

/// <summary>
/// NATS service server.
/// </summary>
public class NatsSvcServer : INatsSvcServer
{
    private readonly ILogger _logger;
    private readonly string _id;
    private readonly INatsConnection _nats;
    private readonly NatsSvcConfig _config;
    private readonly Channel<SvcMsg> _channel;
    private readonly Task _taskMsgLoop;
    private readonly List<SvcListener> _svcListeners = new();
    private readonly ConcurrentDictionary<string, NatsSvcEndpointBase> _endPoints = new();
    private readonly string _started;
    private readonly CancellationTokenSource _cts;
    private readonly CancellationToken _creationToken;

    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcServer"/>.
    /// </summary>
    /// <param name="nats">NATS connection.</param>
    /// <param name="config">Service configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the service creation requests. It does not affect the running service, which runs until <see cref="StopAsync"/> or <see cref="DisposeAsync"/> is called.</param>
    public NatsSvcServer(INatsConnection nats, NatsSvcConfig config, CancellationToken cancellationToken)
    {
        _logger = nats.Opts.LoggerFactory.CreateLogger<NatsSvcServer>();
        _id = Nuid.NewNuid();
        _nats = nats;
        _config = config;
        _creationToken = cancellationToken;

        // Not linked to the creation token: that token also ended the discovery listeners
        // and made StopAsync a no-op, while the endpoints kept serving.
        _cts = new CancellationTokenSource();
        _channel = Channel.CreateBounded<SvcMsg>(32);
        _taskMsgLoop = Task.Run(MsgLoop);
        _started = DateTimeOffset.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff'Z'");
    }

    /// <summary>
    /// Stop the service.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the stop operation.</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        // Return here when disposed if we already stopped
        if (_cts.Token.IsCancellationRequested)
            return;

        await StopCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a new endpoint.
    /// </summary>
    /// <param name="handler">Callback for handling incoming messages.</param>
    /// <param name="name">Optional endpoint name.</param>
    /// <param name="subject">Optional endpoint subject.</param>
    /// <param name="queueGroup">Queue group name (defaults to service group's).</param>
    /// <param name="metadata">Optional endpoint metadata.</param>
    /// <param name="serializer">Serializer to use when deserializing incoming messages (defaults to connection's serializer).</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to stop the endpoint.</param>
    /// <typeparam name="T">Serialization type for messages received.</typeparam>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// One of name or subject must be specified.
    /// </remarks>
    public ValueTask AddEndpointAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name = default, string? subject = default, string? queueGroup = default, IDictionary<string, string>? metadata = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default)
    {
        queueGroup ??= _config.UseQueueGroup ? _config.QueueGroup : null;
        return AddEndpointInternalAsync<T>(handler, name, subject, queueGroup, metadata, serializer, cancellationToken);
    }

    /// <summary>
    /// Removes an endpoint by name, draining and disposing it.
    /// </summary>
    /// <param name="name">Name of the endpoint to remove.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to bound the drain operation's PING/PONG fence</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// The endpoint stops receiving new messages, then any messages it has already received are
    /// handled before it is disposed. Other endpoints on this service are unaffected.
    /// <para>
    /// Endpoints added through a <see cref="Group"/> are removed by their endpoint name, without
    /// the group prefix.
    /// </para>
    /// </remarks>
    /// <exception cref="NatsSvcException">There is no endpoint with that name.</exception>
    public async ValueTask RemoveEndpointAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_endPoints.TryRemove(name, out var ep))
        {
            throw new NatsSvcException($"Endpoint '{name}' does not exist");
        }

        try
        {
            await ep.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ep.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Adds a new service group with optional queue group.
    /// </summary>
    /// <param name="name">Name of the group.</param>
    /// <param name="queueGroup">Queue group name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> may be used to cancel th call in the future.</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    public ValueTask<Group> AddGroupAsync(string name, string? queueGroup = default, CancellationToken cancellationToken = default)
    {
        var group = new Group(this, name, queueGroup, cancellationToken);
        return new ValueTask<Group>(group);
    }

    /// <inheritdoc />
    public StatsResponse GetStats()
    {
        var endPoints = _endPoints.Select(ep =>
        {
            JsonNode? statsData;
            try
            {
                statsData = _config.StatsHandler?.Invoke(ep.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling stats handler for {Endpoint}", ep.Key);
                statsData = null;
            }

            return new EndpointStats
            {
                Name = ep.Key,
                Subject = ep.Value.Subject,
                QueueGroup = ep.Value.QueueGroup!,
                Data = statsData!,
                ProcessingTime = ep.Value.ProcessingTime,
                NumRequests = ep.Value.Requests,
                NumErrors = ep.Value.Errors,
                LastError = ep.Value.LastError ?? string.Empty,
                AverageProcessingTime = ep.Value.AverageProcessingTime,
            };
        }).ToList();

        var response = new StatsResponse
        {
            Name = _config.Name,
            Id = _id,
            Version = _config.Version,
            Metadata = _config.Metadata!,
            Endpoints = endPoints,
            Started = _started,
        };
        return response;
    }

    /// <inheritdoc />
    public InfoResponse GetInfo()
    {
        var endPoints = _endPoints.Select(ep => new EndpointInfo
        {
            Name = ep.Key,
            Subject = ep.Value.Subject,
            QueueGroup = ep.Value.QueueGroup!,
            Metadata = ep.Value.Metadata!,
        }).ToList();

        var infoResponse = new InfoResponse
        {
            Name = _config.Name,
            Id = _id,
            Version = _config.Version,
            Description = _config.Description!,
            Metadata = _config.Metadata!,
            Endpoints = endPoints,
        };
        return infoResponse;
    }

    /// <summary>
    /// Stop the service.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopCoreAsync(_cts.Token);
        GC.SuppressFinalize(this);
    }

    // The creation token bounds this call only. The listeners subscribe with _cts, the
    // service's own lifetime token, so the creation token is not passed down to them; a
    // subscribe still waiting when the call is abandoned is ended by AbortStartAsync
    // cancelling _cts.
    internal async ValueTask StartAsync()
    {
        var name = _config.Name;
        var cancellationToken = _creationToken;

        try
        {
            foreach (var svcType in new[] { SvcMsgType.Ping, SvcMsgType.Info, SvcMsgType.Stats })
            {
                var type = svcType.ToString().ToUpper();
                foreach (var subject in new[] { $"$SRV.{type}", $"$SRV.{type}.{name}", $"$SRV.{type}.{name}.{_id}" })
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // for discovery subjects do not use a queue group
                    var svcListener = new SvcListener(_logger, _nats, _channel, svcType, subject, default, _cts.Token);
                    _svcListeners.Add(svcListener);
                    await svcListener.StartAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            await AbortStartAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Undo a start that was cancelled or failed part way. The caller gets the exception
    // and never sees the service, so nothing it started may stay subscribed.
    private async ValueTask AbortStartAsync()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();

        foreach (var listener in _svcListeners)
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }

        _svcListeners.Clear();

        try
        {
            await _taskMsgLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // intentionally canceled
        }
    }

    private async ValueTask AddEndpointInternalAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name, string? subject, string? queueGroup, IDictionary<string, string>? metadata, INatsDeserialize<T>? serializer, CancellationToken cancellationToken)
    {
        serializer ??= _nats.Opts.SerializerRegistry.GetDeserializer<T>();

        var epSubject = subject ?? name ?? throw new NatsSvcException("Either name or subject must be specified");
        var epName = name ?? epSubject.Replace(".", "-");

        var ep = new NatsSvcEndpoint<T>(_nats, queueGroup, epName, handler, epSubject, metadata, serializer, opts: default, cancellationToken);

        if (!_endPoints.TryAdd(epName, ep))
        {
            await using (ep)
            {
                throw new NatsSvcException($"Endpoint '{name}' already exists");
            }
        }

        await ep.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MsgLoop()
    {
        await foreach (var svcMsg in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                var type = svcMsg.MsgType;
                var data = svcMsg.Msg.Data;

                if (type == SvcMsgType.Ping)
                {
                    using (data)
                    {
                        // empty request payload
                    }

                    await svcMsg.Msg.ReplyAsync(
                        data: new PingResponse
                        {
                            Name = _config.Name,
                            Id = _id,
                            Version = _config.Version,
                            Metadata = _config.Metadata!,
                        },
                        serializer: NatsSrvJsonSerializer<PingResponse>.Default,
                        cancellationToken: _cts.Token);
                }
                else if (type == SvcMsgType.Info)
                {
                    using (data)
                    {
                        // empty request payload
                    }

                    await svcMsg.Msg.ReplyAsync(
                        data: GetInfo(),
                        serializer: NatsSrvJsonSerializer<InfoResponse>.Default,
                        cancellationToken: _cts.Token);
                }
                else if (type == SvcMsgType.Stats)
                {
                    using (data)
                    {
                        // empty request payload
                    }

                    await svcMsg.Msg.ReplyAsync(
                        data: GetStats(),
                        serializer: NatsSrvJsonSerializer<StatsResponse>.Default,
                        cancellationToken: _cts.Token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message loop error");
            }
        }
    }

    /// <summary>
    /// Single owner of the service shutdown sequence. The order below is an invariant:
    /// endpoints are drained before the service channel is completed and before the
    /// service token is cancelled, so that requests already being handled can still
    /// finish and reply.
    /// </summary>
    private async ValueTask StopCoreAsync(CancellationToken cancellationToken)
    {
        foreach (var listener in _svcListeners)
        {
            await listener.DisposeAsync();
        }

        // Drain the endpoints: each endpoint unsubscribes, fences delivery with a
        // PING/PONG round-trip and waits for its handler to finish, so requests
        // that are already in flight are not dropped.
        // This MUST happen before _cts.Cancel() below, otherwise the endpoint
        // handler loop is cancelled before it can drain its channel.
        //
        // The endpoints are stopped together. One at a time, an endpoint stays
        // subscribed and keeps taking new requests for as long as the ones before
        // it are busy, and the fence timeouts add up when the server does not answer.
        //
        // Without an open connection there is nothing to fence and the PING would
        // only wait out its timeout, so the endpoints are disposed straight away.
        // That still handles what they have already received.
        var fence = _nats.ConnectionState == NatsConnectionState.Open;
        try
        {
            await Task.WhenAll(_endPoints.Values.Select(ep => StopEndpointAsync(ep, fence, cancellationToken))).ConfigureAwait(false);
        }
        finally
        {
            // An endpoint that fails to stop must not leave the service half stopped.
            _channel.Writer.TryComplete();

            _cts.Cancel();
            try
            {
                await _taskMsgLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // intentionally canceled
            }
        }
    }

    private async Task StopEndpointAsync(NatsSvcEndpointBase ep, bool fence, CancellationToken cancellationToken)
    {
        try
        {
            if (fence)
                await ep.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ep.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// NATS service group.
    /// </summary>
    public class Group
    {
        private readonly NatsSvcServer _server;
        private readonly string _dot;

        /// <summary>
        /// Creates a new instance of <see cref="Group"/>.
        /// </summary>
        /// <param name="server">Service instance.</param>
        /// <param name="groupName">Group name.</param>
        /// <param name="queueGroup">Optional queue group.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> may be used to cancel th call in the future.</param>
        public Group(NatsSvcServer server, string groupName, string? queueGroup = default, CancellationToken cancellationToken = default)
        {
            ValidateGroupName(groupName);
            _server = server;
            GroupName = groupName;
            QueueGroup = queueGroup;
            _dot = GroupName.Length == 0 ? string.Empty : ".";
        }

        public string GroupName { get; }

        public string? QueueGroup { get; }

        /// <summary>
        /// Adds a new endpoint.
        /// </summary>
        /// <param name="handler">Callback for handling incoming messages.</param>
        /// <param name="name">Optional endpoint name.</param>
        /// <param name="subject">Optional endpoint subject.</param>
        /// <param name="queueGroup">Queue group name (defaults to service group's).</param>
        /// <param name="metadata">Optional endpoint metadata.</param>
        /// <param name="serializer">Serializer to use when deserializing incoming messages (defaults to connection's serializer).</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to stop the endpoint.</param>
        /// <typeparam name="T">Serialization type for messages received.</typeparam>
        /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
        /// <remarks>
        /// One of name or subject must be specified.
        /// </remarks>
        public ValueTask AddEndpointAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name = default, string? subject = default, string? queueGroup = default, IDictionary<string, string>? metadata = default, INatsDeserialize<T>? serializer = default, CancellationToken cancellationToken = default)
        {
            subject ??= name;
            var epSubject = subject != null ? $"{GroupName}{_dot}{subject}" : null;
            queueGroup ??= QueueGroup ?? (_server._config.UseQueueGroup ? _server._config.QueueGroup : null);
            serializer ??= _server._nats.Opts.SerializerRegistry.GetDeserializer<T>();
            return _server.AddEndpointInternalAsync(handler, name, epSubject, queueGroup, metadata, serializer, cancellationToken);
        }

        /// <summary>
        /// Adds a new service group with optional queue group.
        /// </summary>
        /// <param name="name">Name of the group.</param>
        /// <param name="queueGroup">Optional queue group name.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> may be used to cancel th call in the future.</param>
        /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
        public ValueTask<Group> AddGroupAsync(string name, string? queueGroup = default, CancellationToken cancellationToken = default)
        {
            var groupName = $"{GroupName}{_dot}{name}";
            return _server.AddGroupAsync(groupName, queueGroup, cancellationToken);
        }

        private void ValidateGroupName(string groupName)
        {
            foreach (var c in groupName)
            {
                switch (c)
                {
                case '>':
                    throw new NatsSvcException("Invalid group name (can't have '>' wildcard in group name)");
                case '\r' or '\n' or ' ':
                    throw new NatsSvcException("Invalid group name (must be a valid NATS subject)");
                }
            }
        }
    }
}
