using NATS.Client.Core;

var nc = new NatsConnection();

List<Task> tasks = new();

var bytes = new byte[1024];
for (int i = 0; i < 10; i++)
{
    tasks.Add(Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                await nc.PublishAsync("foo", bytes);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }));
}

await Task.WhenAll(tasks);
