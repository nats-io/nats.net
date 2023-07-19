using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace NATS.Client.JetStream.Models;

/// <summary>
/// A response from the JetStream $JS.API.STREAM.CREATE API
/// </summary>
public record StreamCreateResponse : StreamInfo
{
}
