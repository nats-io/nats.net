// Copyright 2021 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at:
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using NATS.Client.Internals.SimpleJSON;

namespace NATS.Client.JetStream
{
    public sealed class PublishAck : ApiResponse
    {
        public string Stream { get; private set; }
        public ulong Seq { get; private set; }
        public string Domain { get; private set; }
        public bool Duplicate { get; private set; }

        public PublishAck(Msg msg) : base(msg, false)
        {
            Init();
        }

        public PublishAck(string json) : base(json)
        {
            Init();
        }

        private void Init()
        {
            ThrowOnHasError();
            Stream = JsonNode[ApiConstants.Stream].Value;
            if (Stream.Length == 0)
            {
                throw new NATSException("Invalid JetStream ack.");
            }

            JSONNode possible = JsonNode[ApiConstants.Seq];
            if (possible.IsNumber)
            {
                Seq = possible.AsUlong;
            }
            else
            {
                throw new NATSException("Invalid JetStream ack.");
            }

            Domain = JsonNode[ApiConstants.Domain].Value;
            Duplicate = JsonNode[ApiConstants.Duplicate].AsBool;
        }

        public override string ToString()
        {
            return $"Stream: {Stream}, Domain: {Domain}, Seq: {Seq}, Duplicate: {Duplicate}";
        }
    }
}
