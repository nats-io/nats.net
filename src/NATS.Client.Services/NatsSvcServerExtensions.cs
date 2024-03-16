namespace NATS.Client.Services
{
    public interface IEndpointRegistrar
    {
        Task RegisterEndpointsAsync(INatsSvcServer service, CancellationToken cancellationToken = default);
    }
}

namespace NATS.Client.Services
{
    public interface IEndpointRegistrarFactory
    {
        IEndpointRegistrar CreateRegistrar();
    }
}
