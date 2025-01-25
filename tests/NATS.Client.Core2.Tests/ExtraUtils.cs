using System.Runtime.CompilerServices;
using System.Threading.Channels;

// ReSharper disable once CheckNamespace
namespace NATS.Client.Core2.Tests.ExtraUtils.FrameworkPolyfillExtensions;

internal static class ChannelReaderExtensions
{
#if NETFRAMEWORK
    // This is a little annoying; it's copied from NATS.Client.Core.Internal.NetStandardExtensions
    // just to keep Rider happy, see also https://youtrack.jetbrains.com/issue/RSRP-499012
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(this ChannelReader<T> reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out var msg))
            {
                yield return msg;
            }
        }
    }
#endif
}
