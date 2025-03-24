using System.Collections;

namespace MultiMaps.Core;

public class MultiValueDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
{
    private const int DefaultCapacity = 64;
    private const float LoadFactorThreshold = 0.75f;

    public int Count;

    internal Bucket<TKey, TValue>[] Buckets;
    internal int Version;

    private readonly object _syncRoot = new object();

    public MultiValueDictionary(int capacity)
    {
        Buckets = new Bucket<TKey, TValue>[capacity];
    }

    public MultiValueDictionary() : this(DefaultCapacity) { }

    public void Add(TKey key, TValue value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_syncRoot)
        {
            EnsureCapacity();

            int index = GetIndex(key);
            Bucket<TKey, TValue> bucket;
            if (Buckets[index] == null)
            {
                Buckets[index] = new Bucket<TKey, TValue>();
            }
            bucket = Buckets[index];

            var entry = FindEntry(bucket, key);
            if (entry == null)
            {
                entry = new Entry<TKey, TValue>(key);
                entry.Values.Add(value);
                entry.Next = bucket.Head;
                bucket.Head = entry;

                Count++;
                Version++;
            }
            else
            {
                entry.Values.Add(value);
                Version++;
            }
        }
    }

    public IReadOnlyCollection<TValue> GetValues(TKey key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_syncRoot)
        {
            int index = GetIndex(key);
            var bucket = Buckets[index];
            if (bucket == null) return Array.Empty<TValue>();

            var entry = FindEntry(bucket, key);
            if (entry == null)
            {
                return Array.Empty<TValue>();
            }
            else
            {
                return entry.Values.AsReadOnly();
            }
        }
    }

    public bool RemoveValue(TKey key, TValue value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_syncRoot)
        {
            int index = GetIndex(key);
            var bucket = Buckets[index];
            if (bucket == null) return false;

            var entry = FindEntry(bucket, key);
            if (entry == null) return false;

            bool removed = entry.Values.Remove(value);
            if (removed)
            {
                Version++;
                if (entry.Values.Count == 0)
                {
                    RemoveKeyInternal(bucket, key);
                }
            }
            return removed;
        }
    }

    public bool RemoveKey(TKey key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        lock (_syncRoot)
        {
            int index = GetIndex(key);
            var bucket = Buckets[index];
            if (bucket == null) return false;

            return RemoveKeyInternal(bucket, key);
        }
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return new MultiValueDictionaryEnumerator<TKey, TValue>(this, _syncRoot);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void EnsureCapacity()
    {
        float loadFactor = (float)Count / Buckets.Length;
        if (loadFactor >= LoadFactorThreshold)
        {
            Resize(Buckets.Length * 2);
        }
    }

    private void Resize(int newCapacity)
    {
        var oldBuckets = Buckets;
        Buckets = new Bucket<TKey, TValue>[newCapacity];
        Count = 0;
        Version++;

        foreach (var bucket in oldBuckets)
        {
            if (bucket == null) continue;

            var entry = bucket.Head;
            while (entry != null)
            {
                foreach (var value in entry.Values)
                {
                    Add(entry.Key, value);
                }

                entry = entry.Next;
            }
        }
    }

    private int GetIndex(TKey key)
    {
        return Math.Abs(key!.GetHashCode()) % Buckets.Length;
    }

    private Entry<TKey, TValue>? FindEntry(Bucket<TKey, TValue> bucket, TKey key)
    {
        var current = bucket.Head;
        while (current != null)
        {
            if (current.Key!.Equals(key))
            {
                return current;
            }

            current = current.Next;
        }
        return null;
    }

    private bool RemoveKeyInternal(Bucket<TKey, TValue> bucket, TKey key)
    {
        Entry<TKey, TValue>? previous = null;
        var current = bucket.Head;

        while (current != null)
        {
            if (current.Key!.Equals(key))
            {
                if (previous == null)
                {
                    bucket.Head = current.Next;
                }
                else
                {
                    previous.Next = current.Next;
                }

                Count--;
                Version++;
                return true;
            }
            previous = current;
            current = current.Next;
        }
        return false;
    }
}