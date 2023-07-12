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

public record StreamState
{
    [JsonPropertyName("messages")]
    public ulong Messages { get; set; }

    [JsonPropertyName("bytes")]
    public ulong Bytes { get; set; }

    [JsonPropertyName("first_seq")]
    public ulong FirstSeq { get; set; }

    [JsonPropertyName("last_seq")]
    public ulong LastSeq { get; set; }

    [JsonPropertyName("consumer_count")]
    public long ConsumerCount { get; set; }

    [JsonPropertyName("num_subjects")]
    public long NumSubjects { get; set; }

    [JsonPropertyName("num_deleted")]
    public long NumDeleted { get; set; }

    [JsonPropertyName("first_ts")]
    public DateTimeOffset FirstTs { get; set; }

    [JsonPropertyName("last_ts")]
    public DateTimeOffset LastTs { get; set; }

    [JsonPropertyName("subjects")]
    public string Subjects { get; set; }

    [JsonPropertyName("deleted")]
    public string Deleted { get; set; }

    [JsonPropertyName("lost")]
    public string Lost { get; set; }
}
