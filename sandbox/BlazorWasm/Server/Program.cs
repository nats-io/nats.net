using BlazorWasm.Server.NatsServices;
using Example.Core;
using NATS.Client.Hosting;
using OpenTelemetry.Trace;

TracingSetup.SetSandboxEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(o => o
        .AddAspNetCoreInstrumentation()
        .AddNatsInstrumentation()
        .AddOtlpExporter());

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddNats(configureOpts: opt => opt with { Url = "localhost:4222", Name = "BlazorServer" });
builder.Services.AddHostedService<WeatherForecastService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapFallbackToFile("index.html");
app.Run();
