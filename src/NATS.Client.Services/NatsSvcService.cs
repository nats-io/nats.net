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
    private readonly ConcurrentDictionary<string, NatsSvcEndPoint> _endPoints = new();
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
    }

    public async ValueTask AddEndPointAsync(string name, Func<NatsSvcMsg, ValueTask> handler, string? subject = default, IDictionary<string, string>? metadata = default, CancellationToken cancellationToken = default)
    {
        var ep = new NatsSvcEndPoint(_nats, _config, name, handler, subject, metadata, opts: default, cancellationToken);

        if (!_endPoints.TryAdd(name, ep))
            throw new NatsSvcException($"Endpoint '{name}' already exists");

        await ep.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var listener in _svcListeners)
        {
            await listener.DisposeAsync();
        }

        _channel.Writer.TryComplete();

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
                            statsData = _config.StatsHandler();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error calling stats handler for {Endpoint}", ep.Key);
                            Console.WriteLine($"[STATS] error calling stats handler for {ep.Key}: {ex.Message}");
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
}
