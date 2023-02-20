using System.Text;

namespace NATS.Client.Core;

/// <summary>
/// Represents Subject/QueueGroup of NATS
/// </summary>
public readonly struct NatsKey : IEquatable<NatsKey>
{
    public readonly string Key;
    internal readonly byte[]? buffer; // subject with space padding.

    internal int LengthWithSpacePadding => Key.Length + 1;

    public NatsKey(string key)
        : this(key, false)
    {
    }

    internal NatsKey(string key, bool withoutEncoding)
    {
        this.Key = key;
        if (withoutEncoding)
        {
            this.buffer = null;
        }
        else
        {
            this.buffer = Encoding.ASCII.GetBytes(key + " ");
        }
    }

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    public bool Equals(NatsKey other)
    {
        return this.Key == other.Key;
    }

    public override string ToString()
    {
        return Key;
    }
}
