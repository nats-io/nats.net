using System;
using NATS.Client.Core;

namespace NATS.Client.OpenTelemetry;

/// <summary>
/// Extension methods for <see cref="NatsInstrumentationOptions"/>.
/// </summary>
public static class NatsInstrumentationOptionsExtensions
{
    /// <summary>
    /// Restricts tracing to operations whose subject matches the given NATS subject patterns.
    /// </summary>
    /// <param name="options">The options to configure.</param>
    /// <param name="include">
    /// Subject patterns to trace. An operation is traced only if its subject matches at least one
    /// pattern. When <c>null</c> or empty, every subject is eligible (still subject to <paramref name="exclude"/>).
    /// </param>
    /// <param name="exclude">
    /// Subject patterns to skip. An operation matching any of these is not traced, even when it also
    /// matches an include pattern. A common use is dropping inbox traffic with <c>_INBOX.&gt;</c>.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <remarks>
    /// Patterns use NATS subject wildcards: <c>*</c> matches a single token and <c>&gt;</c> matches one or
    /// more trailing tokens. The resulting predicate is combined (logical AND) with any existing
    /// <see cref="NatsInstrumentationOptions.Filter"/>, so a previously configured filter still applies.
    /// </remarks>
    public static NatsInstrumentationOptions FilterSubjects(
        this NatsInstrumentationOptions options,
        string[]? include = null,
        string[]? exclude = null)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var includeTokens = Tokenize(include);
        var excludeTokens = Tokenize(exclude);

        // Nothing to filter on; leave any existing filter untouched.
        if (includeTokens is null && excludeTokens is null)
            return options;

        var previous = options.Filter;
        options.Filter = context =>
        {
            if (previous is not null && !previous(context))
                return false;

            var subject = context.Subject;

            if (excludeTokens is not null)
            {
                foreach (var pattern in excludeTokens)
                {
                    if (Matches(subject, pattern))
                        return false;
                }
            }

            if (includeTokens is not null)
            {
                foreach (var pattern in includeTokens)
                {
                    if (Matches(subject, pattern))
                        return true;
                }

                return false;
            }

            return true;
        };

        return options;
    }

    private static string[][]? Tokenize(string[]? patterns)
    {
        if (patterns is null || patterns.Length == 0)
            return null;

        var result = new string[patterns.Length][];
        for (var i = 0; i < patterns.Length; i++)
            result[i] = patterns[i].Split('.');

        return result;
    }

    // NATS subject match: '*' matches exactly one token, '>' matches one or more trailing tokens.
    // Walks the subject token by token instead of allocating a string[] per message; the pattern is
    // already tokenized once at configuration time.
    private static bool Matches(string subject, string[] pattern)
    {
        var remaining = subject.AsSpan();
        var exhausted = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == ">")
                return !exhausted; // '>' needs at least one remaining subject token; exhausted means the subject is fully consumed

            if (exhausted)
                return false; // pattern has more tokens than the subject

            var dot = remaining.IndexOf('.');
            ReadOnlySpan<char> token;
            if (dot < 0)
            {
                token = remaining;
                exhausted = true; // this was the subject's last token
            }
            else
            {
                token = remaining.Slice(0, dot);
                remaining = remaining.Slice(dot + 1);
            }

            if (pattern[i] != "*" && !pattern[i].AsSpan().SequenceEqual(token))
                return false;
        }

        return exhausted; // subject and pattern must have the same token count
    }
}
