// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_Elsewhere
#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable IDE0007
#pragma warning disable IDE0008

using NATS.Client.Core;

namespace NATS.Net.DocsExamples.Advanced;

public class ServerErrorsPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Advanced.ServerErrorsPage");

        {
            #region server-error
            await using NatsConnection nc = new NatsConnection();

            nc.ServerError += (sender, args) =>
            {
                Console.WriteLine($"Server error: {args.Error}");
                return default;
            };
            #endregion
        }

        {
            #region server-error-kind
            await using NatsConnection nc = new NatsConnection();

            nc.ServerError += (sender, args) =>
            {
                switch (args.Kind)
                {
                case NatsServerErrorKind.PermissionsViolation:
                    Console.WriteLine($"Permission denied: {args.Error}");
                    break;
                case NatsServerErrorKind.AuthorizationViolation:
                case NatsServerErrorKind.AuthenticationExpired:
                case NatsServerErrorKind.AuthenticationRevoked:
                case NatsServerErrorKind.AccountAuthenticationExpired:
                    Console.WriteLine($"Auth problem: {args.Error}");
                    break;
                default:
                    Console.WriteLine($"Server error ({args.Kind}): {args.Error}");
                    break;
                }

                return default;
            };
            #endregion
        }
    }
}
