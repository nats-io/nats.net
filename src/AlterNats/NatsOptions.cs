using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlterNats;

/// <summary>
/// Immutable options for NatsConnection, you can configure via `with` operator.
/// </summary>
/// <param name="Url"></param>
/// <param name="ConnectOptions"></param>
/// <param name="TlsOptions"></param>
/// <param name="Serializer"></param>
/// <param name="LoggerFactory"></param>
/// <param name="WriterBufferSize"></param>
/// <param name="ReaderBufferSize"></param>
/// <param name="UseThreadPoolCallback"></param>
/// <param name="InboxPrefix"></param>
/// <param name="NoRandomize"></param>
/// <param name="PingInterval"></param>
/// <param name="MaxPingOut"></param>
/// <param name="ReconnectWait"></param>
/// <param name="ReconnectJitter"></param>
/// <param name="ConnectTimeout"></param>
/// <param name="CommandPoolSize"></param>
/// <param name="RequestTimeout"></param>
public sealed record NatsOptions
(
    string Url,
    ConnectOptions ConnectOptions,
    TlsOptions TlsOptions,
    INatsSerializer Serializer,
    ILoggerFactory LoggerFactory,
    int WriterBufferSize,
    int ReaderBufferSize,
    bool UseThreadPoolCallback,
    string InboxPrefix,
    bool NoRandomize,
    TimeSpan PingInterval,
    int MaxPingOut,
    TimeSpan ReconnectWait,
    TimeSpan ReconnectJitter,
    TimeSpan ConnectTimeout,
    int CommandPoolSize,
    TimeSpan RequestTimeout
)
{
    public static readonly NatsOptions Default = new(
        Url: "nats://localhost:4222",
        ConnectOptions: ConnectOptions.Default,
        TlsOptions: TlsOptions.Default,
        Serializer: new JsonNatsSerializer(new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
        LoggerFactory: NullLoggerFactory.Instance,
        WriterBufferSize: 65534, // 32767
        ReaderBufferSize: 1048576,
        UseThreadPoolCallback: false,
        InboxPrefix: "_INBOX.",
        NoRandomize: false,
        PingInterval: TimeSpan.FromMinutes(2),
        MaxPingOut: 2,
        ReconnectWait: TimeSpan.FromSeconds(2),
        ReconnectJitter: TimeSpan.FromMilliseconds(100),
        ConnectTimeout: TimeSpan.FromSeconds(2),
        CommandPoolSize: 256,
        RequestTimeout: TimeSpan.FromMinutes(1)
    );

    internal NatsUri[] GetSeedUris()
    {
        var urls = Url.Split(',');
        return NoRandomize
            ? urls.Select(x => new NatsUri(x, true)).Distinct().ToArray()
            : urls.Select(x => new NatsUri(x, true)).OrderBy(_ => Guid.NewGuid()).Distinct().ToArray();
    }
}
