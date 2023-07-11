using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

public record StreamCreateRequest
{
    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [Required]
    [JsonPropertyName("subjects")]
    public string[] Subjects { get; set; }

    [JsonPropertyName("retention")]
    public string Retention { get; set; } = "limits";

    [JsonPropertyName("max_consumers")]
    public long MaxConsumers { get; set; } = -1;

    [JsonPropertyName("max_msgs_per_subject")]
    public long MaxMsgsPerSubject { get; set; } = -1;

    [JsonPropertyName("max_msgs")]
    public long MaxMsgs { get; set; } = -1;

    [JsonPropertyName("max_bytes")]
    public long MaxBytes { get; set; } = -1;

    [JsonPropertyName("max_age")]
    public long MaxAge { get; set; } = 0;

    [JsonPropertyName("max_msg_size")]
    public long MaxMsgSize { get; set; } = -1;

    [JsonPropertyName("storage")]
    public string Storage { get; set; } = "file";

    [JsonPropertyName("discard")]
    public string Discard { get; set; } = "old";

    [JsonPropertyName("num_replicas")]
    public long NumReplicas { get; set; } = 1;

    [JsonPropertyName("duplicate_window")]
    public long DuplicateWindow { get; set; } = 120000000000;

    [JsonPropertyName("sealed")]
    public bool Sealed { get; set; } = false;

    [JsonPropertyName("deny_delete")]
    public bool DenyDelete { get; set; } = false;

    [JsonPropertyName("deny_purge")]
    public bool DenyPurge { get; set; } = false;

    [JsonPropertyName("allow_rollup_hdrs")]
    public bool AllowRollupHdrs { get; set; } = false;

    [JsonPropertyName("allow_direct")]
    public bool AllowDirect { get; set; } = false;

    [JsonPropertyName("mirror_direct")]
    public bool MirrorDirect { get; set; } = false;
}
