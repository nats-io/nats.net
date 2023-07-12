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

public record AccountLimits
{
    [JsonPropertyName("max_memory")]
    public long MaxMemory { get; set; }

    [JsonPropertyName("max_storage")]
    public long MaxStorage { get; set; }

    [JsonPropertyName("max_streams")]
    public long MaxStreams { get; set; }

    [JsonPropertyName("max_consumers")]
    public long MaxConsumers { get; set; }

    [JsonPropertyName("max_ack_pending")]
    public long MaxAckPending { get; set; }

    [JsonPropertyName("memory_max_stream_bytes")]
    public long MemoryMaxStreamBytes { get; set; }

    [JsonPropertyName("storage_max_stream_bytes")]
    public long StorageMaxStreamBytes { get; set; }

    [JsonPropertyName("max_bytes_required")]
    public bool MaxBytesRequired { get; set; }
}
