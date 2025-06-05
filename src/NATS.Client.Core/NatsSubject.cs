using System.Text.RegularExpressions;

namespace NATS.Client.Core;

public record NatsSubject
{
    private string? _compiledSubject = null;

    internal NatsSubject(string template, Dictionary<string, object> values)
    {
        Values = values;
        Template = template;
    }

    internal NatsSubject(string subject)
        : this(subject, [])
    {
        _compiledSubject = subject;
    }

    public NatsSubject(string subject, string key, object value)
        : this(subject, new Dictionary<string, object>() { { key, value } })
    {
    }

    internal string? Template { get; set; }

    internal Dictionary<string, object> Values { get; private set; }

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

    internal bool IsInbox(string inboxPrefix) => !string.IsNullOrEmpty(inboxPrefix)
        && inboxPrefix != null
        && (Template?.StartsWith(inboxPrefix, StringComparison.Ordinal) == true ||
            ToString().StartsWith(inboxPrefix, StringComparison.Ordinal));
}
