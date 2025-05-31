namespace NATS.Client.Core;

public record NatsPubOpts
{
    /// <summary>
    /// Obsolete option historically used to control when PublishAsync returned
    /// No longer has any effect
    /// This option method will be removed in a future release
    /// </summary>
    [Obsolete("No longer has any effect")]
    public bool? WaitUntilSent { get; init; }

    /// <summary>
    /// Obsolete callback historically used for handling serialization errors
    /// All errors are now thrown as exceptions in PublishAsync
    /// This option method will be removed in a future release
    /// </summary>
    [Obsolete("All errors are now thrown as exceptions in PublishAsync")]
    public Action<Exception>? ErrorHandler { get; init; }

    public string? Subject { get; set; } = null;
    public string? SubjectTemplate { get; set; } = null;
    public string? ReplyTo { get; set; } = null;
    public string? ReplyToTemplate { get; set; } = null;
    internal string? InboxPrefix { get; set; } = null;
    internal bool UsesInbox => !string.IsNullOrEmpty(InboxPrefix) && Subject?.StartsWith(InboxPrefix, StringComparison.Ordinal) == true;

    internal string SantisedSubject()
    {
        // to avoid long span names and low cardinality, only take the first two tokens
        var tokens = Subject.Split('.');
        return tokens.Length < 2 ? Subject : $"{tokens[0]}.{tokens[1]}";
    }
}
