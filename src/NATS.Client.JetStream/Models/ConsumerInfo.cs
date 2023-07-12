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

public record ConsumerInfo
{
    [JsonPropertyName("stream_name")]
    public string StreamName { get; set; }

    [JsonPropertyName("config")]
    public string Config { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }

    [JsonPropertyName("delivered")]
    public string Delivered { get; set; }

    [JsonPropertyName("ack_floor")]
    public string AckFloor { get; set; }

    [JsonPropertyName("num_pending")]
    public ulong NumPending { get; set; }

    [JsonPropertyName("num_waiting")]
    public long NumWaiting { get; set; }

    [JsonPropertyName("num_ack_pending")]
    public long NumAckPending { get; set; }

    [JsonPropertyName("num_redelivered")]
    public long NumRedelivered { get; set; }

    [JsonPropertyName("cluster")]
    public string Cluster { get; set; }

    [JsonPropertyName("push_bound")]
    public bool PushBound { get; set; }
}
