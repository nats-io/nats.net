using System.Runtime.CompilerServices;

namespace NATS.Client.Core.Internal;

/// <summary>
/// Validates NATS subjects and queue groups at the API level.
/// </summary>
/// <remarks>
/// Subject validation prevents protocol-breaking whitespace characters from being sent to the server.
/// This validation is performed at the top-level API (Publish/Subscribe) for immediate error feedback.
/// </remarks>
internal static class SubjectValidator
{
    // Used for subject/replyTo/queueGroup validation to prevent protocol-breaking whitespace.
    // Static field ensures zero allocations per call. SearchValues (NET8+) uses SIMD vectorization;
    // char[] (older TFMs) uses optimized IndexOfAny for <=5 chars.
#if NET8_0_OR_GREATER
    private static readonly System.Buffers.SearchValues<char> WhitespaceChars = System.Buffers.SearchValues.Create([' ', '\r', '\n', '\t']);
#else
    private static readonly char[] WhitespaceChars = [' ', '\r', '\n', '\t'];
#endif

    /// <summary>
    /// Validates a subject string for whitespace characters.
    /// </summary>
    /// <param name="subject">The subject to validate.</param>
    /// <exception cref="NatsException">Thrown when the subject is empty or contains whitespace.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateSubject(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            ThrowOnBadSubject();
        }

        ValidateSubjectSpan(subject.AsSpan());
    }

    /// <summary>
    /// Validates a reply-to string for whitespace characters (if not null).
    /// </summary>
    /// <param name="replyTo">The reply-to to validate, or null.</param>
    /// <exception cref="NatsException">Thrown when the reply-to contains whitespace.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateReplyTo(string? replyTo)
    {
        if (replyTo != null)
        {
            if (replyTo.Length == 0)
            {
                ThrowOnBadSubject();
            }

            ValidateSubjectSpan(replyTo.AsSpan());
        }
    }

    /// <summary>
    /// Validates a queue group string for whitespace characters (if not null).
    /// </summary>
    /// <param name="queueGroup">The queue group to validate, or null.</param>
    /// <exception cref="NatsException">Thrown when the queue group is empty or contains whitespace.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateQueueGroup(string? queueGroup)
    {
        if (queueGroup != null)
        {
            if (queueGroup.Length == 0 || queueGroup.AsSpan().IndexOfAny(WhitespaceChars) >= 0)
            {
                ThrowOnBadQueueGroup();
            }
        }
    }

    // Uses adaptive algorithm: manual loop for short subjects (< 16 chars) with
    // branch-predicted early exit, SIMD-optimized IndexOfAny for longer subjects.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateSubjectSpan(ReadOnlySpan<char> subject)
    {
        if (subject.Length < 16)
        {
            // Fast path for short subjects - branch predictor learns most chars are > ' '
            foreach (var c in subject)
            {
                if (c <= ' ' && (c == ' ' || c == '\t' || c == '\r' || c == '\n'))
                {
                    ThrowOnBadSubject();
                }
            }
        }
        else
        {
            // SIMD path for long subjects
            if (subject.IndexOfAny(WhitespaceChars) >= 0)
            {
                ThrowOnBadSubject();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOnBadSubject() => throw new NatsException("Subject is invalid.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOnBadQueueGroup() => throw new NatsException("Queue group is invalid.");
}
