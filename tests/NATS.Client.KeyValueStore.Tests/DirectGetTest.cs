using NATS.Client.Core.Tests;

namespace NATS.Client.KeyValueStore.Tests;

public class DirectGetTest(ITestOutputHelper output)
{
    [Fact]
    public async Task API_subject_test()
    {
        await using var server = await NatsServer.StartJSAsync();
        var (nats1, proxy) = server.CreateProxiedClientConnection();
        await using var nats = nats1;

        var js = new NatsJSContext(nats);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        // default
        {
            var kv = new NatsKVContext(js);

            var store = await kv.CreateStoreAsync(new NatsKVConfig("b1"), cancellationToken);
            await store.PutAsync("x", 1, cancellationToken: cancellationToken);
            await store.PutAsync("x", 2, cancellationToken: cancellationToken);

            await proxy.FlushFramesAsync(nats);

            var entry = await store.GetEntryAsync<int>("x", cancellationToken: cancellationToken);
            Assert.Equal(2, entry.Value);

            var proto = proxy.ClientFrames[0].Message;
            Assert.StartsWith("PUB $JS.API.DIRECT.GET.KV_b1 _INBOX.", proto);
            Assert.EndsWith("""␍␊{"last_by_subj":"$KV.b1.x"}""", proto);
            foreach (var proxyFrame in proxy.ClientFrames)
            {
                output.WriteLine(proxyFrame.Message);
            }
        }

        // key in api subject
        {
            var kv = new NatsKVContext(js, new NatsKVOpts { UseDirectGetApiWithKeysInSubject = true });

            var store = await kv.CreateStoreAsync(new NatsKVConfig("b1"), cancellationToken);
            await store.PutAsync("x", 1, cancellationToken: cancellationToken);
            await store.PutAsync("x", 2, cancellationToken: cancellationToken);

            await proxy.FlushFramesAsync(nats);

            var entry = await store.GetEntryAsync<int>("x", cancellationToken: cancellationToken);
            Assert.Equal(2, entry.Value);

            var proto = proxy.ClientFrames[0].Message;
            Assert.StartsWith("PUB $JS.API.DIRECT.GET.KV_b1.$KV.b1.x _INBOX.", proto);
            Assert.EndsWith(""" 0␍␊""", proto);
            foreach (var proxyFrame in proxy.ClientFrames)
            {
                output.WriteLine(proxyFrame.Message);
            }
        }
    }
}
