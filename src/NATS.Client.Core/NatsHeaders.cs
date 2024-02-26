using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;

namespace NATS.Client.Core;

/// <summary>
/// Represents a wrapper for RequestHeaders and ResponseHeaders.
/// </summary>
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:Elements should appear in the correct order", Justification = "Keep class format as is for reference")]
[SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1504:All accessors should be single-line or multi-line", Justification = "Keep class format as is for reference")]
[SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1516:Elements should be separated by blank line", Justification = "Keep class format as is for reference")]
[SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1513:Closing brace should be followed by blank line", Justification = "Keep class format as is for reference")]
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1214:Readonly fields should appear before non-readonly fields", Justification = "Keep class format as is for reference")]
public class NatsHeaders : IDictionary<string, StringValues>
{
    public enum Messages
    {
        Text,
        IdleHeartbeat,
        BadRequest,
        ConsumerDeleted,
        ConsumerIsPushBased,
        NoMessages,
        RequestTimeout,
        MessageSizeExceedsMaxBytes,
    }

    // Uses C# compiler's optimization for static byte[] data
    // string.Join(",", Encoding.ASCII.GetBytes("Idle Heartbeat"))
    internal static ReadOnlySpan<byte> MessageIdleHeartbeat => new byte[] { 73, 100, 108, 101, 32, 72, 101, 97, 114, 116, 98, 101, 97, 116 };
    internal static readonly string MessageIdleHeartbeatStr = "Idle Heartbeat";

    // Bad Request
    internal static ReadOnlySpan<byte> MessageBadRequest => new byte[] { 66, 97, 100, 32, 82, 101, 113, 117, 101, 115, 116 };
    internal static readonly string MessageBadRequestStr = "Bad Request";

    // Consumer Deleted
    internal static ReadOnlySpan<byte> MessageConsumerDeleted => new byte[] { 67, 111, 110, 115, 117, 109, 101, 114, 32, 68, 101, 108, 101, 116, 101, 100 };
    internal static readonly string MessageConsumerDeletedStr = "Consumer Deleted";

    // Consumer is push based
    internal static ReadOnlySpan<byte> MessageConsumerIsPushBased => new byte[] { 67, 111, 110, 115, 117, 109, 101, 114, 32, 105, 115, 32, 112, 117, 115, 104, 32, 98, 97, 115, 101, 100 };
    internal static readonly string MessageConsumerIsPushBasedStr = "Consumer is push based";

    // No Messages
    internal static ReadOnlySpan<byte> MessageNoMessages => new byte[] { 78, 111, 32, 77, 101, 115, 115, 97, 103, 101, 115 };
    internal static readonly string MessageNoMessagesStr = "No Messages";

    // Request Timeout
    internal static ReadOnlySpan<byte> MessageRequestTimeout => new byte[] { 82, 101, 113, 117, 101, 115, 116, 32, 84, 105, 109, 101, 111, 117, 116 };
    internal static readonly string MessageRequestTimeoutStr = "Request Timeout";

    // Message Size Exceeds MaxBytes
    internal static ReadOnlySpan<byte> MessageMessageSizeExceedsMaxBytes => new byte[] { 77, 101, 115, 115, 97, 103, 101, 32, 83, 105, 122, 101, 32, 69, 120, 99, 101, 101, 100, 115, 32, 77, 97, 120, 66, 121, 116, 101, 115 };
    internal static readonly string MessageMessageSizeExceedsMaxBytesStr = "Message Size Exceeds MaxBytes";

    private static readonly string[] EmptyKeys = Array.Empty<string>();
    private static readonly StringValues[] EmptyValues = Array.Empty<StringValues>();

    // Pre-box
    private static readonly IEnumerator<KeyValuePair<string, StringValues>> EmptyIEnumeratorType = default(Enumerator);
    private static readonly IEnumerator EmptyIEnumerator = default(Enumerator);

    private int _readonly = 0;

    public int Version => 1;

    public int Code { get; internal set; }

    public string MessageText { get; internal set; } = string.Empty;

    public Messages Message { get; internal set; } = Messages.Text;

    internal Activity? Activity { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="NatsHeaders"/>.
    /// </summary>
    public NatsHeaders()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="NatsHeaders"/>.
    /// </summary>
    /// <param name="store">The value to use as the backing store.</param>
    public NatsHeaders(Dictionary<string, StringValues>? store)
    {
        Store = store;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="NatsHeaders"/>.
    /// </summary>
    /// <param name="capacity">The initial number of headers that this instance can contain.</param>
    public NatsHeaders(int capacity)
    {
        EnsureStore(capacity);
    }

    private Dictionary<string, StringValues>? Store { get; set; }

    [MemberNotNull(nameof(Store))]
    private void EnsureStore(int capacity)
    {
        if (Store == null)
        {
            Store = new Dictionary<string, StringValues>(capacity, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Get or sets the associated value from the collection as a single string.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <returns>the associated value from the collection as a StringValues or StringValues.Empty if the key is not present.</returns>
    public StringValues this[string key]
    {
        get
        {
            if (Store == null)
            {
                return StringValues.Empty;
            }

            if (TryGetValue(key, out var value))
            {
                return value;
            }

            return StringValues.Empty;
        }
        set
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            ThrowIfReadOnly();

            if (value.Count == 0)
            {
                Store?.Remove(key);
            }
            else
            {
                EnsureStore(1);
                Store[key] = value;
            }
        }
    }

    StringValues IDictionary<string, StringValues>.this[string key]
    {
        get => this[key];
        set
        {
            ThrowIfReadOnly();
            this[key] = value;
        }
    }

    /// <summary>
    /// Gets the number of elements contained in the <see cref="NatsHeaders" />;.
    /// </summary>
    /// <returns>The number of elements contained in the <see cref="NatsHeaders" />.</returns>
    public int Count => Store?.Count ?? 0;

    /// <summary>
    /// Gets a value that indicates whether the <see cref="NatsHeaders" /> is in read-only mode.
    /// </summary>
    /// <returns>true if the <see cref="NatsHeaders" /> is in read-only mode; otherwise, false.</returns>
    public bool IsReadOnly => Volatile.Read(ref _readonly) == 1;

    /// <summary>
    /// Gets the collection of HTTP header names in this instance.
    /// </summary>
    public ICollection<string> Keys
    {
        get
        {
            if (Store == null)
            {
                return EmptyKeys;
            }

            return Store.Keys;
        }
    }

    /// <summary>
    /// Gets the collection of HTTP header values in this instance.
    /// </summary>
    public ICollection<StringValues> Values
    {
        get
        {
            if (Store == null)
            {
                return EmptyValues;
            }

            return Store.Values;
        }
    }

    /// <summary>
    /// Adds a new header item to the collection.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(KeyValuePair<string, StringValues> item)
    {
        if (item.Key == null)
        {
            throw new ArgumentException("The key is null");
        }

        ThrowIfReadOnly();
        EnsureStore(1);
        Store.Add(item.Key, item.Value);
    }

    /// <summary>
    /// Adds the given header and values to the collection.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <param name="value">The header values.</param>
    public void Add(string key, StringValues value)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        ThrowIfReadOnly();
        EnsureStore(1);
        Store.Add(key, value);
    }

    /// <summary>
    /// Clears the entire list of objects.
    /// </summary>
    public void Clear()
    {
        ThrowIfReadOnly();
        Store?.Clear();
    }

    /// <summary>
    /// Returns a value indicating whether the specified object occurs within this collection.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>true if the specified object occurs within this collection; otherwise, false.</returns>
    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        if (Store == null ||
            !Store.TryGetValue(item.Key, out var value) ||
            !StringValues.Equals(value, item.Value))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether the <see cref="NatsHeaders" /> contains a specific key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>true if the <see cref="NatsHeaders" /> contains a specific key; otherwise, false.</returns>
    public bool ContainsKey(string key)
    {
        if (Store == null)
        {
            return false;
        }

        return Store.ContainsKey(key);
    }

    /// <summary>
    /// Copies the <see cref="NatsHeaders" /> elements to a one-dimensional Array instance at the specified index.
    /// </summary>
    /// <param name="array">The one-dimensional Array that is the destination of the specified objects copied from the <see cref="NatsHeaders" />.</param>
    /// <param name="arrayIndex">The zero-based index in <paramref name="array" /> at which copying begins.</param>
    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        if (Store == null)
        {
            return;
        }

        foreach (var item in Store)
        {
            array[arrayIndex] = item;
            arrayIndex++;
        }
    }

    /// <summary>
    /// Removes the given item from the the collection.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>true if the specified object was removed from the collection; otherwise, false.</returns>
    public bool Remove(KeyValuePair<string, StringValues> item)
    {
        ThrowIfReadOnly();
        if (Store == null)
        {
            return false;
        }

        if (Store.TryGetValue(item.Key, out var value) && StringValues.Equals(item.Value, value))
        {
            return Store.Remove(item.Key);
        }

        return false;
    }

    /// <summary>
    /// Removes the given header from the collection.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <returns>true if the specified object was removed from the collection; otherwise, false.</returns>
    public bool Remove(string key)
    {
        ThrowIfReadOnly();
        if (Store == null)
        {
            return false;
        }

        return Store.Remove(key);
    }

    /// <summary>
    /// Retrieves a value from the dictionary.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <param name="value">The value.</param>
    /// <returns>true if the <see cref="NatsHeaders" /> contains the key; otherwise, false.</returns>
    public bool TryGetValue(string key, out StringValues value)
    {
        if (Store == null)
        {
            value = default(StringValues);
            return false;
        }

        return Store.TryGetValue(key, out value);
    }

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>An <see cref="Enumerator" /> object that can be used to iterate through the collection.</returns>
    public Enumerator GetEnumerator()
    {
        if (Store == null || Store.Count == 0)
        {
            // Non-boxed Enumerator
            return default;
        }

        return new Enumerator(Store.GetEnumerator());
    }

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>An <see cref="IEnumerator" /> object that can be used to iterate through the collection.</returns>
    IEnumerator<KeyValuePair<string, StringValues>> IEnumerable<KeyValuePair<string, StringValues>>.GetEnumerator()
    {
        if (Store == null || Store.Count == 0)
        {
            // Non-boxed Enumerator
            return EmptyIEnumeratorType;
        }

        return Store.GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>An <see cref="IEnumerator" /> object that can be used to iterate through the collection.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        if (Store == null || Store.Count == 0)
        {
            // Non-boxed Enumerator
            return EmptyIEnumerator;
        }

        return Store.GetEnumerator();
    }

    internal void SetReadOnly() => Interlocked.Exchange(ref _readonly, 1);

    private void ThrowIfReadOnly()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("The response headers cannot be modified because the response has already started.");
        }
    }

    /// <summary>
    /// Enumerates a <see cref="NatsHeaders"/>.
    /// </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<string, StringValues>>
    {
        // Do NOT make this readonly, or MoveNext will not work
        private Dictionary<string, StringValues>.Enumerator _dictionaryEnumerator;
        private readonly bool _notEmpty;

        internal Enumerator(Dictionary<string, StringValues>.Enumerator dictionaryEnumerator)
        {
            _dictionaryEnumerator = dictionaryEnumerator;
            _notEmpty = true;
        }

        /// <summary>
        /// Advances the enumerator to the next element of the <see cref="NatsHeaders"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the enumerator was successfully advanced to the next element;
        /// <see langword="false"/> if the enumerator has passed the end of the collection.</returns>
        public bool MoveNext()
        {
            if (_notEmpty)
            {
                return _dictionaryEnumerator.MoveNext();
            }

            return false;
        }

        /// <summary>
        /// Gets the element at the current position of the enumerator.
        /// </summary>
        public KeyValuePair<string, StringValues> Current
        {
            get
            {
                if (_notEmpty)
                {
                    return _dictionaryEnumerator.Current;
                }

                return default(KeyValuePair<string, StringValues>);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        object IEnumerator.Current
        {
            get
            {
                return Current;
            }
        }

        void IEnumerator.Reset()
        {
            if (_notEmpty)
            {
                ((IEnumerator)_dictionaryEnumerator).Reset();
            }
        }
    }
}
