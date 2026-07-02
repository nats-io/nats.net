using Microsoft.Extensions.Primitives;

namespace NATS.Client.Core;

/// <summary>
/// Represents NATS message headers as a dictionary of string keys and <see cref="StringValues"/> values.
/// </summary>
public interface INatsHeaders : IDictionary<string, StringValues>
{
}
