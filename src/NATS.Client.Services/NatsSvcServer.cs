using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.Services.Internal;
using NATS.Client.Services.Models;

namespace NATS.Client.Services;

/// <summary>
/// NATS service server.
/// </summary>
public class NatsSvcServer : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly string _id;
    private readonly NatsConnection _nats;
    private readonly NatsSvcConfig _config;
    private readonly CancellationToken _cancellationToken;
    private readonly Channel<SvcMsg> _channel;
    private readonly Task _taskMsgLoop;
    private readonly List<SvcListener> _svcListeners = new();
    private readonly ConcurrentDictionary<string, INatsSvcEndpoint> _endPoints = new();
    private readonly string _started;
    private readonly CancellationTokenSource _cts;

    /// <summary>
    /// Creates a new instance of <see cref="NatsSvcServer"/>.
    /// </summary>
    /// <param name="nats">NATS connection.</param>
    /// <param name="config">Service configuration.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the service creation requests.</param>
    public NatsSvcServer(NatsConnection nats, NatsSvcConfig config, CancellationToken cancellationToken)
    {
        _logger = nats.Opts.LoggerFactory.CreateLogger<NatsSvcServer>();
        _id = NuidWriter.NewNuid();
        _nats = nats;
        _config = config;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationToken = _cts.Token;
        _channel = Channel.CreateBounded<SvcMsg>(32);
        _taskMsgLoop = Task.Run(MsgLoop);
        _started = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
    }

    /// <summary>
    /// Stop the service.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to cancel the stop operation.</param>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        foreach (var listener in _svcListeners)
        {
            await listener.DisposeAsync();
        }

        // Drain buffers
        await _nats.PingAsync(cancellationToken);

        foreach (var ep in _endPoints.Values)
        {
            await ep.DisposeAsync();
        }

        _channel.Writer.TryComplete();

        _cts.Cancel();

        await _taskMsgLoop;
    }

    /// <summary>
    /// Adds a new endpoint.
    /// </summary>
    /// <param name="handler">Callback for handling incoming messages.</param>
    /// <param name="name">Optional endpoint name.</param>
    /// <param name="subject">Optional endpoint subject.</param>
    /// <param name="metadata">Optional endpoint metadata.</param>
    /// <param name="serializer">Serializer to use for the message type.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to stop the endpoint.</param>
    /// <typeparam name="T">Serialization type for messages received.</typeparam>
    /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// One of name or subject must be specified.
    /// </remarks>
    public ValueTask AddEndpointAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name = default, string? subject = default, IDictionary<string, string>? metadata = default, INatsSerializer<T>? serializer = default, CancellationToken cancellationToken = default) =>
        AddEndpointInternalAsync<T>(handler, name, subject, _config.QueueGroup, metadata, serializer, cancellationToken);

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
        return ValueTask.FromResult(group);
    }

    /// <summary>
    /// Stop the service.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(_cancellationToken);
        GC.SuppressFinalize(this);
    }

    internal async ValueTask StartAsync()
    {
        var name = _config.Name;

        foreach (var svcType in new[] { SvcMsgType.Ping, SvcMsgType.Info, SvcMsgType.Stats })
        {
            var type = svcType.ToString().ToUpper();
            foreach (var subject in new[] { $"$SRV.{type}", $"$SRV.{type}.{name}", $"$SRV.{type}.{name}.{_id}" })
            {
                var svcListener = new SvcListener(_nats, _channel, svcType, subject, _config.QueueGroup, _cancellationToken);
                await svcListener.StartAsync();
                _svcListeners.Add(svcListener);
            }
        }
    }

    private async ValueTask AddEndpointInternalAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name, string? subject, string? queueGroup, IDictionary<string, string>? metadata, INatsSerializer<T>? serializer, CancellationToken cancellationToken)
    {
        serializer ??= _nats.Opts.Serializers.GetSerializer<T>();

        var epSubject = subject ?? name ?? throw new NatsSvcException("Either name or subject must be specified");
        var epName = name ?? epSubject;

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
        await foreach (var svcMsg in _channel.Reader.ReadAllAsync(_cancellationToken))
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
                        data: new PingResponse { Name = _config.Name, Id = _id, Version = _config.Version, },
                        serializer: NatsSrvJsonSerializer<PingResponse>.Default,
                        cancellationToken: _cancellationToken);
                }
                else if (type == SvcMsgType.Info)
                {
                    using (data)
                    {
                        // empty request payload
                    }

                    var endPoints = _endPoints.Select(ep => new EndpointInfo
                    {
                        Name = ep.Key,
                        Subject = ep.Value.Subject,
                        QueueGroup = ep.Value.QueueGroup!,
                        Metadata = ep.Value.Metadata!,
                    }).ToList();

                    await svcMsg.Msg.ReplyAsync(
                        new InfoResponse
                        {
                            Name = _config.Name,
                            Id = _id,
                            Version = _config.Version,
                            Description = _config.Description!,
                            Metadata = _config.Metadata!,
                            Endpoints = endPoints,
                        },
                        serializer: NatsSrvJsonSerializer<InfoResponse>.Default,
                        cancellationToken: _cancellationToken);
                }
                else if (type == SvcMsgType.Stats)
                {
                    using (data)
                    {
                        // empty request payload
                    }

                    var endPoints = _endPoints.Select(ep =>
                    {
                        JsonNode? statsData;
                        try
                        {
                            statsData = _config.StatsHandler?.Invoke();
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
                            LastError = ep.Value.LastError!,
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

                    await svcMsg.Msg.ReplyAsync(
                        response,
                        serializer: NatsSrvJsonSerializer<StatsResponse>.Default,
                        cancellationToken: _cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message loop error");
            }
        }
    }

    /// <summary>
    /// NATS service group.
    /// </summary>
    public class Group
    {
        private readonly NatsSvcServer _server;
        private readonly CancellationToken _cancellationToken;
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
            _cancellationToken = cancellationToken;
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
        /// <param name="metadata">Optional endpoint metadata.</param>
        /// <param name="serializer">Serializer to use for the message type.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to stop the endpoint.</param>
        /// <typeparam name="T">Serialization type for messages received.</typeparam>
        /// <returns>A <seealso cref="ValueTask"/> representing the asynchronous operation.</returns>
        /// <remarks>
        /// One of name or subject must be specified.
        /// </remarks>
        public ValueTask AddEndpointAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name = default, string? subject = default, IDictionary<string, string>? metadata = default, INatsSerializer<T>? serializer = default, CancellationToken cancellationToken = default)
        {
            serializer ??= _server._nats.Opts.Serializers.GetSerializer<T>();

            var epName = name != null ? $"{GroupName}{_dot}{name}" : null;
            var epSubject = subject != null ? $"{GroupName}{_dot}{subject}" : null;
            var queueGroup = QueueGroup ?? _server._config.QueueGroup;
            return _server.AddEndpointInternalAsync(handler, epName, epSubject, queueGroup, metadata, serializer, cancellationToken);
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
