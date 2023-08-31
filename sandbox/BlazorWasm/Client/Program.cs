using BlazorWasm.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NATS.Client.Core;
using NATS.Client.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddNats(configureOpts: opt => opt with { Url = "ws://localhost:4280", Name = "BlazorClient" });

await builder.Build().RunAsync();
