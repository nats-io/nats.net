using BlazorWasm.Shared;
using NATS.Client.Core;

namespace BlazorWasm.Server.NatsServices;

public class WeatherForecastService : IHostedService, IAsyncDisposable
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching",
    };

    private readonly ILogger<WeatherForecastService> _logger;
    private readonly INatsConnection _natsConnection;
    private INatsSub<object>? _replySubscription;
    private Task? _replyTask;

    public WeatherForecastService(ILogger<WeatherForecastService> logger, INatsConnection natsConnection)
    {
        _logger = logger;
        _natsConnection = natsConnection;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _replySubscription = await _natsConnection.SubscribeAsync<object>("weather", cancellationToken: cancellationToken);
        _replyTask = Task.Run(
            async () =>
            {
                await foreach (var msg in _replySubscription.Msgs.ReadAllAsync(cancellationToken))
                {
                    var forecasts = Enumerable.Range(1, 5).Select(index => new WeatherForecast
                    {
                        Date = DateTime.Now.AddDays(index),
                        TemperatureC = Random.Shared.Next(-20, 55),
                        Summary = Summaries[Random.Shared.Next(Summaries.Length)],
                    }).ToArray();
                    await msg.ReplyAsync(forecasts, cancellationToken: cancellationToken);
                }
            },
            cancellationToken);
        _logger.LogInformation("Weather Forecast Services is running");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Weather Forecast Services is stopping");
        if (_replySubscription != null)
            await _replySubscription.UnsubscribeAsync();
        if (_replyTask != null)
            await _replyTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_replySubscription != null)
            await _replySubscription.DisposeAsync();
        if (_replyTask != null)
            await _replyTask;
    }
}
