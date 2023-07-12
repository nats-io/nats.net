// Copyright 2023 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Text.Json.Serialization;

namespace NATS.Client.JetStream.Models;

public record StreamConfiguration
{
    [JsonPropertyName("retention")]
    public string Retention { get; set; }

    [JsonPropertyName("storage")]
    public string Storage { get; set; }

    [JsonPropertyName("discard")]
    public string Discard { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("subjects")]
    public string Subjects { get; set; }

    [JsonPropertyName("max_consumers")]
    public long MaxConsumers { get; set; }

    [JsonPropertyName("max_msgs")]
    public long MaxMsgs { get; set; }

    [JsonPropertyName("max_msgs_per_subject")]
    public long MaxMsgsPerSubject { get; set; }

    [JsonPropertyName("max_bytes")]
    public long MaxBytes { get; set; }

    [JsonPropertyName("max_age")]
    public TimeSpan MaxAge { get; set; }

    [JsonPropertyName("max_msg_size")]
    public long MaxMsgSize { get; set; }

    [JsonPropertyName("num_replicas")]
    public int NumReplicas { get; set; }

    [JsonPropertyName("no_ack")]
    public bool NoAck { get; set; }

    [JsonPropertyName("template_owner")]
    public string TemplateOwner { get; set; }

    [JsonPropertyName("duplicate_window")]
    public TimeSpan DuplicateWindow { get; set; }

    [JsonPropertyName("placement")]
    public string Placement { get; set; }

    [JsonPropertyName("republish")]
    public string Republish { get; set; }

    [JsonPropertyName("mirror")]
    public string Mirror { get; set; }

    [JsonPropertyName("sources")]
    public string Sources { get; set; }

    [JsonPropertyName("sealed")]
    public bool Sealed { get; set; }

    [JsonPropertyName("allow_rollup_hdrs")]
    public bool AllowRollupHdrs { get; set; }

    [JsonPropertyName("allow_direct")]
    public bool AllowDirect { get; set; }

    [JsonPropertyName("mirror_direct")]
    public bool MirrorDirect { get; set; }

    [JsonPropertyName("deny_delete")]
    public bool DenyDelete { get; set; }

    [JsonPropertyName("deny_purge")]
    public bool DenyPurge { get; set; }

    [JsonPropertyName("discard_new_per_subject")]
    public bool DiscardNewPerSubject { get; set; }

    [JsonPropertyName("metadata")]
    public string Metadata { get; set; }
}
