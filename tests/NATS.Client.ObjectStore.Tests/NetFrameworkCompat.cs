using System.Security.Cryptography;

namespace NATS.Client.ObjectStore.Tests;

// Stand-ins for BCL APIs the test sources use that do not exist on .NET Framework. These are
// deliberately named so they do not shadow the types they stand in for: an earlier version
// aliased File/Random/SHA256 assembly-wide, which meant a call to any member the shim did not
// cover still compiled on Linux (net8.0 only there) and broke on Windows CI alone. They compile
// on every target framework and forward to the real API where there is one, so a call site reads
// the same everywhere and the net481 leg is the only thing switching underneath.
internal static class Sha256Compat
{
    public static byte[] HashData(byte[] source)
    {
#if NETFRAMEWORK
        using var sha = SHA256.Create();
        return sha.ComputeHash(source);
#else
        return SHA256.HashData(source);
#endif
    }
}

internal static class RandomCompat
{
#if NETFRAMEWORK
    // Random.Shared does not exist on .NET Framework, and the parameterless Random seeds from
    // Environment.TickCount (~15ms resolution), so two xunit worker threads first touching this
    // within the same tick would otherwise produce identical byte sequences for their payloads.
    [ThreadStatic]
    private static Random? _shared;

    public static Random Shared => _shared ??= new Random(Guid.NewGuid().GetHashCode());
#else
    public static Random Shared => Random.Shared;
#endif
}

internal static class FileCompat
{
    public static FileStream OpenRead(string path) => File.OpenRead(path);

    public static FileStream OpenWrite(string path) => File.OpenWrite(path);

#if NETFRAMEWORK
    // File.ReadAllBytesAsync/WriteAllBytesAsync are net-core only. Going through an async
    // FileStream rather than wrapping the blocking call keeps the CancellationToken meaningful:
    // callers here run under a test deadline and would otherwise never observe it expiring.
    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await target.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
    }
#else
    public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(path, cancellationToken);

    public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        => File.WriteAllBytesAsync(path, bytes, cancellationToken);
#endif
}
