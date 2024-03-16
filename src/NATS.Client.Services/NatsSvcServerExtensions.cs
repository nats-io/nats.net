namespace NATS.Client.Services
{
    public interface INatsSvcEndpointRegistrar
    {
        Task RegisterEndpointsAsync(INatsSvcServer service, CancellationToken cancellationToken = default);

        public static INatsSvcEndpointRegistrar GetRegistrar()
        {
            var factoryType = Type.GetType("NATS.Client.Services.Generated.NatsSvcEndpointRegistrar");
            if (factoryType != null)
                return Activator.CreateInstance(factoryType) as INatsSvcEndpointRegistrar ?? throw new InvalidOperationException("Factory not found. Ensure the source generator executed correctly.");
            return null!;
        }
    }
}
