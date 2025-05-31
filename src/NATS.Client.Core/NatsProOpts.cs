using System;
using System.Collections.Generic;
using System.Text;

namespace NATS.Client.Core;
public record NatsProOpts
{
    internal long Id;

    public int Sid { get; internal set; }
    public string Subject { get; internal set; }
    public int PayloadLength => TotalMessageLength - HeaderLength;
    public string? ReplyTo { get; internal set; }
    public int HeaderLength { get; internal set; }
    public int TotalMessageLength { get; internal set; }
    public int MetaLength { get; internal set; }
    internal string? InboxPrefix { get; set; } = null;
    internal bool UsesInbox => !string.IsNullOrEmpty(InboxPrefix) && Subject?.StartsWith(InboxPrefix, StringComparison.Ordinal) == true;

    internal string SantisedSubject()
    {
        // to avoid long span names and low cardinality, only take the first two tokens
        var tokens = Subject.Split('.');
        return tokens.Length < 2 ? Subject : $"{tokens[0]}.{tokens[1]}";
    }
}
