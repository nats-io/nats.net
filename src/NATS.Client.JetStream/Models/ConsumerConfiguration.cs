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

public record ConsumerConfiguration
{
    [JsonPropertyName("deliver_policy")]
    public string DeliverPolicy { get; set; }

    [JsonPropertyName("ack_policy")]
    public string AckPolicy { get; set; }

    [JsonPropertyName("replay_policy")]
    public string ReplayPolicy { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("durable_name")]
    public string DurableName { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("deliver_subject")]
    public string DeliverSubject { get; set; }

    [JsonPropertyName("deliver_group")]
    public string DeliverGroup { get; set; }

    [JsonPropertyName("filter_subject")]
    public string FilterSubject { get; set; }

    [JsonPropertyName("sample_freq")]
    public string SampleFreq { get; set; }

    [JsonPropertyName("opt_start_time")]
    public DateTimeOffset OptStartTime { get; set; }

    [JsonPropertyName("ack_wait")]
    public TimeSpan AckWait { get; set; }

    [JsonPropertyName("idle_heartbeat")]
    public TimeSpan IdleHeartbeat { get; set; }

    [JsonPropertyName("max_expires")]
    public TimeSpan MaxExpires { get; set; }

    [JsonPropertyName("inactive_threshold")]
    public TimeSpan InactiveThreshold { get; set; }

    [JsonPropertyName("opt_start_seq")]
    public ulong OptStartSeq { get; set; }

    [JsonPropertyName("max_deliver")]
    public int MaxDeliver { get; set; }

    [JsonPropertyName("rate_limit_bps")]
    public ulong RateLimitBps { get; set; }

    [JsonPropertyName("max_ack_pending")]
    public int MaxAckPending { get; set; }

    [JsonPropertyName("max_waiting")]
    public int MaxWaiting { get; set; }

    [JsonPropertyName("max_batch")]
    public int MaxBatch { get; set; }

    [JsonPropertyName("max_bytes")]
    public int MaxBytes { get; set; }

    [JsonPropertyName("num_replicas")]
    public int NumReplicas { get; set; }

    [JsonPropertyName("flow_control")]
    public bool FlowControl { get; set; }

    [JsonPropertyName("headers_only")]
    public bool HeadersOnly { get; set; }

    [JsonPropertyName("mem_storage")]
    public bool MemStorage { get; set; }

    [JsonPropertyName("backoff")]
    public string Backoff { get; set; }

    [JsonPropertyName("metadata")]
    public string Metadata { get; set; }
}
