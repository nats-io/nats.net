namespace NATS.Client.Core;

/// <summary>
/// Recursion guard for default interface method (DIM) pairs on <see cref="INatsSerialize{T}"/>.
/// The interface has two Serialize overloads (with and without headers) whose DIMs call each other,
/// so implementors only need to provide one. If neither is implemented, the DIMs would infinitely
/// recurse; this guard uses a <see cref="ThreadStaticAttribute"/> counter to detect re-entry on
/// the same thread and signal the DIM to throw <see cref="NotImplementedException"/> instead.
/// Thread-safe: each thread has its own counter, so concurrent calls do not interfere.
/// </summary>
internal static class NatsSerializeGuard
{
    [ThreadStatic]
    private static int _depth;

    public static bool TryEnter()
    {
        if (_depth > 0)
        {
            return false;
        }

        _depth++;
        return true;
    }

    public static void Exit() => _depth--;
}

/// <summary>
/// Recursion guard for default interface method (DIM) pairs on <see cref="INatsDeserialize{T}"/>.
/// The interface has two Deserialize overloads (with and without headers) whose DIMs call each other,
/// so implementors only need to provide one. If neither is implemented, the DIMs would infinitely
/// recurse; this guard uses a <see cref="ThreadStaticAttribute"/> counter to detect re-entry on
/// the same thread and signal the DIM to throw <see cref="NotImplementedException"/> instead.
/// Separate from <see cref="NatsSerializeGuard"/> so that serialize and deserialize calls on the
/// same thread do not interfere with each other.
/// </summary>
internal static class NatsDeserializeGuard
{
    [ThreadStatic]
    private static int _depth;

    public static bool TryEnter()
    {
        if (_depth > 0)
        {
            return false;
        }

        _depth++;
        return true;
    }

    public static void Exit() => _depth--;
}
