using System.Text.Json.Nodes;
using System.Threading.Channels;
using NATS.Client.Core;
using NATS.Client.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace Nats.Client.Compat;

public class ServiceCompat
{
    public async Task TestCore(NatsConnection nats, NatsMsg<Memory<byte>> msg, ChannelReader<NatsMsg<Memory<byte>>> reader)
    {
        var svc = new NatsSvcContext(nats);
        var json = JsonNode.Parse(msg.Data.Span);

        // Test.Log($"JSON: {json}");
        var config = json["config"];
        var svcName = config["name"].GetValue<string>();
        var version = config["version"].GetValue<string>();
        var svcConfig = new NatsSvcConfig(svcName, version)
        {
            Description = config["description"].GetValue<string>(),
            Metadata = config["metadata"]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value.GetValue<string>()),
            QueueGroup = config["queue_group"]?.GetValue<string?>() ?? "q",
            StatsHandler = ep => JsonNode.Parse($"{{\"endpoint\":\"{ep.Name}\"}}")!,
        };

        await using var server = await svc.AddServiceAsync(svcConfig);

        var groupNames = config["groups"]
            .AsArray()
            .Select(node => (
                Name: node["name"].GetValue<string>(),
                QueueGroup: node["queue_group"]?.GetValue<string?>()))
            .ToDictionary(kv => kv.Name, kv => kv.QueueGroup);

        Dictionary<string, NatsSvcServer.Group> groups = new();
        foreach (var (key, value) in groupNames)
        {
            var group = await server.AddGroupAsync(key, value);
            groups.Add(key, group);
        }

        Func<NatsSvcMsg<NatsMemoryOwner<byte>>, ValueTask> echoHandler = async m =>
        {
            var memory = new Memory<byte>(new byte[m.Data.Memory.Length]);
            using (m.Data)
                m.Data.Memory.CopyTo(memory);
            await m.ReplyAsync(memory);
        };

        Func<NatsSvcMsg<NatsMemoryOwner<byte>>, ValueTask> faultHandler = async m =>
        {
            await m.ReplyErrorAsync(500, "handler error");
        };

        foreach (var node in config["endpoints"].AsArray())
        {
            var name = node["name"]?.GetValue<string?>();
            var subject = node["subject"]?.GetValue<string?>();
            var metadata = node["metadata"]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value.GetValue<string>());
            var queueGroup = node["queue_group"]?.GetValue<string?>();
            var group = node["group"]?.GetValue<string?>();

            var handler = name == "faulty" ? faultHandler : echoHandler;

            if (group is not null)
            {
                await groups[group].AddEndpointAsync(
                    handler: handler,
                    name: name,
                    subject: subject,
                    queueGroup: queueGroup,
                    metadata: metadata,
                    serializer: default);
            }
            else
            {
                await server.AddEndpointAsync(
                    handler: handler,
                    name: name,
                    subject: subject,
                    queueGroup: queueGroup,
                    metadata: metadata,
                    serializer: default);
            }
        }

        await msg.ReplyAsync();

        msg = await reader.ReadAsync();

        await server.StopAsync();

        if (msg.Subject == "tests.service.core.destroy.command")
        {
            await msg.ReplyAsync();
        }
        else
        {
            Test.Log($"{msg.Subject}");
        }
    }
}
