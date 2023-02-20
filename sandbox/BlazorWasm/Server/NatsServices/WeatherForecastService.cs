using NATS.Client.Core;
using BlazorWasm.Shared;

namespace BlazorWasm.Server.NatsServices;

public class WeatherForecastService : IHostedService
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    private readonly ILogger<WeatherForecastService> _logger;
    private readonly INatsCommand _natsCommand;

    public WeatherForecastService(ILogger<WeatherForecastService> logger, INatsCommand natsCommand)
    {
        _logger = logger;
        _natsCommand = natsCommand;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _natsCommand.SubscribeRequestAsync<object, WeatherForecast[]>("weather", req =>
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            }).ToArray();
        });
        _logger.LogInformation("Weather Forecast Services is running");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Weather Forecast Services is stopping");
        return Task.CompletedTask;
    }
}
