using BlazorWasm.Server.NatsServices;
using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = WebApplication.CreateBuilder(args);

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
