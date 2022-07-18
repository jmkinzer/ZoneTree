﻿using Tenray;
using Tenray.Collections;
using Tenray.WAL;
using ZoneTree.Core;
using ZoneTree.Serializers;
using ZoneTree.WAL;

namespace ZoneTree.Collections;

/// <summary>
/// Persistent Dictionary of dictionary implementation that is combined 
/// with a WriteAheadLog.
/// This class is not thread-safe.
/// </summary>
/// <typeparam name="TKey1"></typeparam>
/// <typeparam name="TKey2"></typeparam>
/// <typeparam name="TValue"></typeparam>
public sealed class DictionaryOfDictionaryWithWAL<TKey1, TKey2, TValue> : IDisposable
{
    readonly long SegmentId;

    readonly string Category;

    readonly IWriteAheadLogProvider WriteAheadLogProvider;

    readonly IWriteAheadLog<TKey1, CombinedValue<TKey2, TValue>> WriteAheadLog;

    Dictionary<TKey1, IDictionary<TKey2, TValue>> Dictionary = new();

    public int Length => Dictionary.Count;

    public IReadOnlyList<TKey1> Keys => Dictionary.Keys.ToArray();

    public DictionaryOfDictionaryWithWAL(
        long segmentId,
        string category,
        IWriteAheadLogProvider writeAheadLogProvider,
        ISerializer<TKey1> key1Serializer,
        ISerializer<TKey2> key2Serializer,
        ISerializer<TValue> valueSerializer)
    {
        WriteAheadLogProvider = writeAheadLogProvider;
        var combinedSerializer = new CombinedSerializer<TKey2, TValue>(key2Serializer, valueSerializer);
        WriteAheadLog = writeAheadLogProvider
            .GetOrCreateWAL(segmentId, category, key1Serializer, combinedSerializer);
        SegmentId = segmentId;
        Category = category;
        LoadFromWriteAheadLog();
    }

    void LoadFromWriteAheadLog()
    {
        var result = WriteAheadLog.ReadLogEntries(false, false);
        if (!result.Success)
        {
            WriteAheadLogProvider.RemoveWAL(SegmentId, Category);
            using var disposeWal = WriteAheadLog;
            throw new WriteAheadLogCorruptionException(SegmentId, result.Exceptions);
        }

        var keys = result.Keys;
        var values = result.Values;
        var len = keys.Count;
        for (var i = 0; i < len; ++i)
        {
            Upsert(keys[i], values[i].Value1, values[i].Value2);
        }
    }

    public bool ContainsKey(in TKey1 key)
    {
        return Dictionary.ContainsKey(key);
    }

    public bool TryGetDictionary(in TKey1 key1, out IDictionary<TKey2, TValue> value)
    {
        return Dictionary.TryGetValue(key1, out value);
    }

    public bool TryGetValue(in TKey1 key1, in TKey2 key2, out TValue value)
    {
        if (Dictionary.TryGetValue(key1, out var dic))
            return dic.TryGetValue(key2, out value);
        value = default;
        return false;
    }

    public bool Upsert(in TKey1 key1, in TKey2 key2, in TValue value)
    {
        if (Dictionary.TryGetValue(key1, out var dic))
        {
            dic.Add(key2, value);
            WriteAheadLog.Append(key1, new CombinedValue<TKey2,TValue>(key2, value));
            return true;
        }
        dic = new Dictionary<TKey2, TValue>
        {
            { key2, value }
        };
        Dictionary[key1] = dic;
        WriteAheadLog.Append(key1, new CombinedValue<TKey2, TValue>(key2, value));
        return false;
    }

    public void Drop()
    {
        WriteAheadLogProvider.RemoveWAL(SegmentId);
        WriteAheadLog?.Drop();
    }

    public void Dispose()
    {
        WriteAheadLogProvider.RemoveWAL(SegmentId);
        WriteAheadLog?.Dispose();
    }
}