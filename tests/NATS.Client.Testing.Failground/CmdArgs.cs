using Microsoft.Extensions.Logging;

namespace NATS.Client.Testing.Failground;

public record CmdArgs
{
    public string? Params { get; set; }

    public string? Id { get; set; }

    public int MaxRetry { get; set; }

    public string? Server { get; set; }

    public bool Trace { get; set; }

    public bool Debug { get; set; }

    public string? Workload { get; set; }

    public string? Error { get; set; }

    public LogLevel LogLevel => Trace ? LogLevel.Trace : Debug ? LogLevel.Debug : LogLevel.Information;

    public bool HasError => Error != null;

    public static CmdArgs Parse(string[] args)
    {
        try
        {
            var cmdName = string.Empty;
            var cmds = new Dictionary<string, string>();
            foreach (var arg in args)
            {
                if (arg.StartsWith("--"))
                {
                    if (cmdName != string.Empty)
                        cmds[cmdName] = string.Empty;

                    cmdName = arg[2..];
                }
                else
                {
                    if (cmdName != string.Empty)
                    {
                        cmds[cmdName] = arg;
                        cmdName = string.Empty;
                    }
                    else
                    {
                        throw new Exception("Error: missing command name");
                    }
                }
            }

            if (cmdName != string.Empty)
            {
                cmds[cmdName] = string.Empty;
            }

            var cmd = new CmdArgs();

            foreach (var (key, value) in cmds)
            {
                switch (key)
                {
                case "server":
                    cmd.Server = value;
                    break;
                case "max-retry":
                    cmd.MaxRetry = int.Parse(value);
                    break;
                case "id":
                    cmd.Id = value;
                    break;
                case "workload":
                    cmd.Workload = value;
                    break;
                case "params":
                    cmd.Params = value;
                    break;
                case "debug":
                    cmd.Debug = true;
                    break;
                case "trace":
                    cmd.Trace = true;
                    break;
                }
            }

            return cmd;
        }
        catch (Exception e)
        {
            return new CmdArgs { Error = e.Message };
        }
    }
}
