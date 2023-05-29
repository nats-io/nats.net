using System.Text;

namespace NATS.Client.Core;

// TODO: Move NatsKey to internal namespace
internal readonly struct NatsKey : IEquatable<NatsKey>
{
    public readonly string Key;
    internal readonly byte[]? Buffer; // subject with space padding.

    internal NatsKey(string key)
        : this(key, false)
    {
    }

    internal NatsKey(string key, bool withoutEncoding)
    {
        Key = key;
        if (withoutEncoding)
        {
            Buffer = null;
        }
        else
        {
            Buffer = Encoding.ASCII.GetBytes(key + " ");
        }
    }

    internal int LengthWithSpacePadding => Key.Length + 1;

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    public bool Equals(NatsKey other)
    {
        return Key == other.Key;
    }

    public override string ToString()
    {
        return Key;
    }
}
