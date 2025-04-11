// ReSharper disable SuggestVarOrType_Elsewhere

using NATS.Client.Services;

#pragma warning disable SA1123
#pragma warning disable SA1124
#pragma warning disable SA1509
#pragma warning disable SA1515
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace NATS.Net.DocsExamples.Services;

public class IntroPage
{
    public async Task Run()
    {
        Console.WriteLine("____________________________________________________________");
        Console.WriteLine("NATS.Net.DocsExamples.Services.IntroPage");

        #region svc
        await using NatsClient nc = new NatsClient();
        INatsSvcContext svc = nc.CreateServicesContext();
        #endregion

        #region add
        await using INatsSvcServer testService = await svc.AddServiceAsync("test", "1.0.0");
        #endregion

        #region endpoint
        await testService.AddEndpointAsync<int>(name: "divide42", handler: async m =>
        {
            // Handle exceptions which may occur during message processing,
            // usually due to serialization errors
            if (m.Exception != null)
            {
                await m.ReplyErrorAsync(500, m.Exception.Message);
                return;
            }

            if (m.Data == 0)
            {
                await m.ReplyErrorAsync(400, "Division by zero");
                return;
            }

            await m.ReplyAsync(42 / m.Data);
        });
        #endregion

        #region grp
        NatsSvcServer.Group grp1 = await testService.AddGroupAsync("grp1");
        await grp1.AddEndpointAsync<int>(name: "ep1", handler: async m =>
        {
            // handle message
        });
        #endregion
    }
}
