namespace NATS.Client.Testing.Failground;

public interface ITest
{
    Task Run(CancellationToken cancellationToken = default);
}
