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

public record AccountTier
{
    [JsonPropertyName("memory")]
    public string Memory { get; set; }

    [JsonPropertyName("storage")]
    public string Storage { get; set; }

    [JsonPropertyName("streams")]
    public string Streams { get; set; }

    [JsonPropertyName("consumers")]
    public string Consumers { get; set; }

    [JsonPropertyName("limits")]
    public string Limits { get; set; }
}
