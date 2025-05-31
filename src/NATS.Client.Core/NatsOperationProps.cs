namespace NATS.Client.Core;

public record NatsOperationProps
{
    public NatsOperationProps(string subject) => SubjectTemplate = subject;

    public NatsOperationProps(string subjectTemplate, string subjectId)
    {
        SubjectTemplate = subjectTemplate;
        if (subjectTemplate.Contains("{{SubjectId}}"))
        {
            SubjectId = subjectId;
        }
    }

    public string? SubjectId { get; private set; } = null;

    public string SubjectTemplate { get; set; }

    public string Subject => SubjectId == null ?
                SubjectTemplate :
                SubjectTemplate.Replace("{{SubjectId}}", SubjectId);

    internal string? InboxPrefix { get; set; } = null;

    internal bool UsesInbox => !string.IsNullOrEmpty(InboxPrefix) && Subject?.StartsWith(InboxPrefix, StringComparison.Ordinal) == true;

    internal string SantisedSubject()
    {
        // to avoid long span names and low cardinality, only take the first two tokens
        var tokens = Subject.Split('.');
        return tokens.Length < 2 ? Subject : $"{tokens[0]}.{tokens[1]}";
    }
}
