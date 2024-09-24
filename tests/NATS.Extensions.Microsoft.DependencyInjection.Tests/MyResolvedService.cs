namespace NATS.Extensions.Microsoft.DependencyInjection.Tests;

internal interface IMyResolvedService
{
    string GetValue();
}

internal class MyResolvedService(string value) : IMyResolvedService
{
    public string GetValue() => value;
}
