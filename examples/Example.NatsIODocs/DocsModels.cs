using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;

namespace Example.NatsIODocs;

// A snake_case JSON serializer registry shared across the docs examples. Records
// keep idiomatic PascalCase properties; the naming policy renders them as
// snake_case on the wire so the payloads match the other language examples on
// docs.nats.io. A per-property [JsonPropertyName] still wins where a field needs
// a specific name (see Order.Timestamp).
public sealed class SnakeCaseJsonSerializerRegistry : INatsSerializerRegistry
{
    public static readonly SnakeCaseJsonSerializerRegistry Default = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new Utc8601DateTimeOffsetConverter() },
    };

    public INatsSerialize<T> GetSerializer<T>() => new NatsJsonSerializer<T>(Options);

    public INatsDeserialize<T> GetDeserializer<T>() => new NatsJsonSerializer<T>(Options);
}

// Writes DateTimeOffset as UTC ISO-8601 with a trailing 'Z' (e.g.
// 2026-05-22T10:14:22Z) instead of the default numeric offset (+00:00), matching
// the timestamp format shown in the other language examples on docs.nats.io.
public sealed class Utc8601DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}

// Message payloads shared across the docs examples. The optional fields on Order
// let an example carry just an id, an id plus customer, or the full order; unset
// fields are omitted from the JSON (see JsonIgnoreCondition.WhenWritingNull).
public record Order(
    string OrderId,
    string? Customer = null,
    int? TotalCents = null,
    [property: JsonPropertyName("ts")] DateTimeOffset? Timestamp = null);

public record OrderCancellation(string OrderId, string Reason);

public record InventoryReply(bool InStock, string Warehouse);

public record ShippingQuote(string Carrier, int QuoteCents);
