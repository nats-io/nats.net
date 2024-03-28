namespace NATS.Client.Services.Controllers;

public class NatsServiceControllerAttribute : Attribute
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string QueueGroup { get; set; }
}
