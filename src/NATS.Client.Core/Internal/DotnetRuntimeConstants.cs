using System.Runtime.InteropServices;

namespace NATS.Client.Core.Internal;

internal static class DotnetRuntimeConstants
{
    public static readonly OSPlatform BrowserPlatform = OSPlatform.Create("Browser");
}
