namespace NATS.Client.Testing.Failground;

public interface ITest
{
    Task Run(string runId, CancellationToken cancellationToken = default);
}
