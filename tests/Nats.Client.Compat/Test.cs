using System.Reflection;
using NATS.Client.Core;

namespace Nats.Client.Compat;

public class Test
{
    public static async Task RunAsync()
    {
        var url = Environment.GetEnvironmentVariable("NATS_URL") ?? NatsOpts.Default.Url;
        var opts = NatsOpts.Default with { Url = url };
        await using var nats = new NatsConnection(opts);

        Log($"Connected to NATS server {url}");

        await using var sub = await nats.SubscribeAsync<Memory<byte>>("tests.>");

        Log($"Subscribed to {sub.Subject}");
        Log($"Ready to receive test messages...");

        await foreach (var msg in sub.Msgs.ReadAllAsync())
        {
            if (msg.Subject == "tests.done")
            {
                Log("Bye");
                break;
            }

            var tokens = msg.Subject.Split('.');
            var suite = tokens[1];
            var test = tokens[2];
            var command = tokens[3];
            var action = tokens[4];

            if (action == "result")
            {
                Log($"{command.ToUpper()}");
            }
            else if (action == "command")
            {
                // We turn the suite name to a class name suffixed by 'Compat' and locate that class.
                // For example suite 'object-store' becomes class name 'ObjectStoreCompat'
                // which is our class containing those tests.
                var typeName = typeof(Test).Namespace + "." + suite.Replace("-", string.Empty) + "compat";
                var type = typeof(Test).Assembly.GetType(typeName, true, true);
                var instance = Activator.CreateInstance(type!);

                // Transform the test name to a method name prefixed by 'Test'
                // so the test 'default-bucket' matches the method 'TestDefaultBucket'
                var methodName = "test" + test.Replace("-", string.Empty);
                var method = type!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                Log($"Testing {suite} {test}...");

                if (method != null)
                {
                    await (Task)method.Invoke(instance, new object[] { nats, msg })!;
                }
                else
                {
                    Log($"Not implemented: {test}");
                    await msg.ReplyAsync();
                }
            }
        }
    }

    public static void Log(string message) => Console.WriteLine($"{DateTime.Now:hh:mm:ss} {message}");
}
