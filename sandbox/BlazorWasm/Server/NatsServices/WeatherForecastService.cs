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
    private NatsReplyHandle? _replyHandle;

    public WeatherForecastService(ILogger<WeatherForecastService> logger, INatsConnection natsConnection)
    {
        _logger = logger;
        _natsConnection = natsConnection;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _replyHandle = await _natsConnection.ReplyAsync<object, WeatherForecast[]>("weather", req =>
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)],
            }).ToArray();
        });
        _logger.LogInformation("Weather Forecast Services is running");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Weather Forecast Services is stopping");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _replyHandle?.DisposeAsync() ?? ValueTask.CompletedTask;
}
