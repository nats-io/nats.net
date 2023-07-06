namespace NATS.Client.Core.Tests;

public class RequestReplyTest
{
    [Fact]
    public async Task Simple_request_reply_test()
    {
        await using var server = new NatsServer();
        await using var nats = server.CreateClientConnection();

        var sub = await nats.SubscribeAsync<int>("foo");
        var reg = sub.Register(async msg =>
        {
            await msg.ReplyAsync(msg.Data * 2);
        });

        for (var i = 0; i < 10; i++)
        {
            var rep = await nats.RequestAsync<int, int>("foo", i);
            Assert.Equal(i * 2, rep?.Data);
        }

        await sub.DisposeAsync();
        await reg;
    }
}
