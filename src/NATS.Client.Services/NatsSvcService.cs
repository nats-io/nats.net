using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.Core.Internal;
using NATS.Client.Services.Internal;
using NATS.Client.Services.Models;

namespace NATS.Client.Services;

public class NatsSvcService : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly string _id;
    private readonly NatsConnection _nats;
    private readonly NatsSvcConfig _config;
    private readonly CancellationToken _cancellationToken;
    private readonly Channel<SvcMsg> _channel;
    private readonly Task _taskMsgLoop;
    private readonly List<SvcListener> _svcListeners = new();
    private readonly ConcurrentDictionary<string, INatsSvcEndPoint> _endPoints = new();
    private readonly string _started;

    public NatsSvcService(NatsConnection nats, NatsSvcConfig config, CancellationToken cancellationToken)
    {
        _logger = nats.Opts.LoggerFactory.CreateLogger<NatsSvcService>();
        _id = NuidWriter.NewNuid();
        _nats = nats;
        _config = config;
        _cancellationToken = cancellationToken;
        _channel = Channel.CreateBounded<SvcMsg>(32);
        _taskMsgLoop = Task.Run(MsgLoop);
        _started = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
    }

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

        await _taskMsgLoop;
    }

    public ValueTask AddEndPointAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name = default, string? subject = default, IDictionary<string, string>? metadata = default, CancellationToken cancellationToken = default) =>
        AddEndPointInternalAsync<T>(handler, name, subject, _config.QueueGroup, metadata, cancellationToken);

    public ValueTask<NatsSvcGroup> AddGroupAsync(string name, string? queueGroup = default, CancellationToken cancellationToken = default)
    {
        var group = new NatsSvcGroup(this, name, queueGroup, cancellationToken);
        return ValueTask.FromResult(group);
    }

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

    private async ValueTask AddEndPointInternalAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name, string? subject, string? queueGroup, IDictionary<string, string>? metadata, CancellationToken cancellationToken)
    {
        var epSubject = subject ?? name ?? throw new NatsSvcException("Either name or subject must be specified");
        var epName = name ?? epSubject;

        var ep = new NatsSvcEndPoint<T>(_nats, queueGroup, epName, handler, epSubject, metadata, opts: default, cancellationToken);

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
                        new PingResponse { Name = _config.Name, Id = _id, Version = _config.Version, },
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
                        cancellationToken: _cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message loop error");
            }
        }
    }

    public class NatsSvcGroup
    {
        private readonly NatsSvcService _service;
        private readonly CancellationToken _cancellationToken;
        private readonly string _dot;

        public NatsSvcGroup(NatsSvcService service, string groupName, string? queueGroup = default, CancellationToken cancellationToken = default)
        {
            ValidateGroupName(groupName);
            _service = service;
            GroupName = groupName;
            QueueGroup = queueGroup;
            _cancellationToken = cancellationToken;
            _dot = GroupName.Length == 0 ? string.Empty : ".";
        }

        public string GroupName { get; }

        public string? QueueGroup { get; }

        public ValueTask AddEndPointAsync<T>(Func<NatsSvcMsg<T>, ValueTask> handler, string? name = default, string? subject = default, IDictionary<string, string>? metadata = default, CancellationToken cancellationToken = default)
        {
            var epName = name != null ? $"{GroupName}{_dot}{name}" : null;
            var epSubject = subject != null ? $"{GroupName}{_dot}{subject}" : null;
            var queueGroup = QueueGroup ?? _service._config.QueueGroup;
            return _service.AddEndPointInternalAsync(handler, epName, epSubject, queueGroup, metadata, cancellationToken);
        }

        public ValueTask<NatsSvcGroup> AddGroupAsync(string name, string? queueGroup, CancellationToken cancellationToken = default)
        {
            var groupName = $"{GroupName}{_dot}{name}";
            return _service.AddGroupAsync(groupName, queueGroup, cancellationToken);
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
