#pragma warning disable SA1403
#pragma warning disable SA1204

// Enable init-only setters on netstandard targets
#if NETSTANDARD
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
