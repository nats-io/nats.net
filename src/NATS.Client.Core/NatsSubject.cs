using System.Text.RegularExpressions;

namespace NATS.Client.Core;

public record NatsSubject
{
    private string? _compiledSubject = null;

    internal NatsSubject(string template, Dictionary<string, object> values, string? inboxPrefix = default)
    {
        Values = values;
        Template = template;
        InboxPrefix = inboxPrefix;
    }

    internal NatsSubject(string subject, string? inboxPrefix = default)
        : this(subject, [], inboxPrefix)
    {
        _compiledSubject = subject;
    }

    internal NatsSubject(string subject, string key, object value, string? inboxPrefix = default)
        : this(subject, new Dictionary<string, object>() { { key, value } }, inboxPrefix)
    {
    }

    internal string? InboxPrefix { get; set; }

    internal string? Template { get; private set; }

    internal Dictionary<string, object> Values { get; private set; }

    internal bool IsInbox => !string.IsNullOrEmpty(InboxPrefix)
        && InboxPrefix != null
        && (Template?.StartsWith(InboxPrefix, StringComparison.Ordinal) == true ||
            ToString().StartsWith(InboxPrefix, StringComparison.Ordinal));

    public override string ToString()
    {
        if (Values.Count == 0 || _compiledSubject != null)
        {
            return _compiledSubject ?? string.Empty;
        }
        else if (Template == null)
        {
            return string.Empty;
        }

        var subject = Template;
        var tokens = Regex.Matches(subject, "{{\\w+}}");
        foreach (Match token in tokens)
        {
            if (Values.TryGetValue(token.Value.Trim('{').Trim('}'), out var value))
            {
                subject.Replace(token.Value, value.ToString());
            }
        }

        _compiledSubject = subject;
        return subject;
    }

    internal string ToSantisedString()
    {
        // to avoid long span names and low cardinality, only take the first two tokens
        var tokens = ToString().Split('.');
        return tokens.Length < 2 ? ToString() : $"{tokens[0]}.{tokens[1]}";
    }
}
