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

public record ServerInfo
{
    [JsonPropertyName("server_id")]
    public string ServerId { get; set; }

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("go")]
    public string Go { get; set; }

    [JsonPropertyName("host")]
    public string Host { get; set; }

    [JsonPropertyName("headers")]
    public bool Headers { get; set; }

    [JsonPropertyName("auth_required")]
    public bool AuthRequired { get; set; }

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; }

    [JsonPropertyName("tls_required")]
    public bool Tls { get; set; }

    [JsonPropertyName("ldm")]
    public bool LameDuckMode { get; set; }

    [JsonPropertyName("jetstream")]
    public bool Jetstream { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("proto")]
    public int Proto { get; set; }

    [JsonPropertyName("max_payload")]
    public long MaxPayload { get; set; }

    [JsonPropertyName("client_id")]
    public int ClientId { get; set; }

    [JsonPropertyName("client_ip")]
    public string ClientIp { get; set; }

    [JsonPropertyName("cluster")]
    public string Cluster { get; set; }

    [JsonPropertyName("connect_urls")]
    public string ConnectUrls { get; set; }
}
