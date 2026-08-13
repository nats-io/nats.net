#if NETFRAMEWORK
// Minimal stand-ins for BCL APIs missing on .NET Framework. The global using aliases below make
// the shims take the place of the real types on net481 only, so the test sources stay identical
// across target frameworks. The aliases apply file-wide, hence the fully qualified names inside
// the shim bodies.
global using File = NATS.Client.ObjectStore.Tests.FileCompat;
global using Random = NATS.Client.ObjectStore.Tests.RandomCompat;
global using SHA256 = NATS.Client.ObjectStore.Tests.Sha256Compat;

namespace NATS.Client.ObjectStore.Tests;

internal static class Sha256Compat
{
    public static byte[] HashData(byte[] source)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(source);
    }
}

internal static class RandomCompat
{
    [ThreadStatic]
    private static System.Random? _shared;

    public static System.Random Shared => _shared ??= new System.Random();
}

internal static class FileCompat
{
    public static FileStream OpenRead(string path) => System.IO.File.OpenRead(path);

    public static FileStream OpenWrite(string path) => System.IO.File.OpenWrite(path);

    public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(System.IO.File.ReadAllBytes(path));

    public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        System.IO.File.WriteAllBytes(path, bytes);
        return Task.CompletedTask;
    }
}
#endif
