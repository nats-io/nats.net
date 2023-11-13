namespace NATS.Client.Testing.Failground;

public interface ITest
{
    Task Run(string runId, CmdArgs args, CancellationToken cancellationToken = default);
}
