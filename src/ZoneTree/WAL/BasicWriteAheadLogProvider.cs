﻿using System.Collections.Concurrent;
using Tenray.ZoneTree.AbstractFileStream;
using Tenray.ZoneTree.Core;

namespace Tenray.ZoneTree.WAL;

public class BasicWriteAheadLogProvider : IWriteAheadLogProvider
{
    readonly ILogger Logger;

    readonly IFileStreamProvider FileStreamProvider;

    readonly ConcurrentDictionary<string, object> WALTable = new();

    public string WalDirectory { get; }

    public BasicWriteAheadLogProvider(
        ILogger logger,
        IFileStreamProvider fileStreamProvider,
        string walDirectory = "data")
    {
        Logger = logger;
        FileStreamProvider = fileStreamProvider;
        WalDirectory = walDirectory;
        FileStreamProvider.CreateDirectory(walDirectory);
    }

    public IWriteAheadLog<TKey, TValue> GetOrCreateWAL<TKey, TValue>(
        long segmentId,
        string category,
        WriteAheadLogOptions options,
        ISerializer<TKey> keySerializer,
        ISerializer<TValue> valueSerializer)
    {
        if (WALTable.TryGetValue(segmentId + category, out var value))
        {
            return (IWriteAheadLog<TKey, TValue>)value;
        }

        (var walPath, var walMode) = 
            DetectWalPathAndWriteAheadLogMode(segmentId, category, options);

        switch (walMode)
        {
            case WriteAheadLogMode.None:
                return new NullWriteAheadLog<TKey, TValue>();
            case WriteAheadLogMode.Sync:
                {
                    var wal = new SyncFileSystemWriteAheadLog<TKey, TValue>(
                        Logger,
                        FileStreamProvider,
                        keySerializer,
                        valueSerializer,
                        walPath)
                    {
                        EnableIncrementalBackup = options.EnableIncrementalBackup
                    };
                    WALTable.TryAdd(segmentId + category, wal);
                    return wal;
                }
            case WriteAheadLogMode.SyncCompressed:
                {
                    var wal = new SyncCompressedFileSystemWriteAheadLog<TKey, TValue>(
                        Logger,
                        FileStreamProvider,
                        keySerializer,
                        valueSerializer,
                        walPath,
                        options.CompressionBlockSize,
                        options.SyncCompressedModeOptions.EnableTailWriterJob,
                        options.SyncCompressedModeOptions.TailWriterJobInterval)
                    {
                        EnableIncrementalBackup = options.EnableIncrementalBackup
                    };
                    WALTable.TryAdd(segmentId + category, wal);
                    return wal;
                }

            case WriteAheadLogMode.AsyncCompressed:
                {
                    var wal = new AsyncCompressedFileSystemWriteAheadLog<TKey, TValue>(
                        Logger,
                        FileStreamProvider,
                        keySerializer,
                        valueSerializer,
                        walPath,
                        options.CompressionBlockSize,
                        options.AsyncCompressedModeOptions.EmptyQueuePollInterval)
                    {
                        EnableIncrementalBackup = options.EnableIncrementalBackup
                    };
                    WALTable.TryAdd(segmentId + category, wal);
                    return wal;
                }
        }
        return null;
    }

    (string walPath, WriteAheadLogMode walMode)
        DetectWalPathAndWriteAheadLogMode(
        long segmentId, string category, WriteAheadLogOptions options)
    {
        var walPath = Path.Combine(WalDirectory, category, segmentId + ".wal.");
        var walMode = options.WriteAheadLogMode;        
        for(var i = 0; i < 3; ++i)
        {
            if (FileStreamProvider.FileExists(walPath + i))
            {
                walMode = (WriteAheadLogMode)i;
                break;
            }
        }
        return (walPath + (int)walMode, walMode);
    }

    public IWriteAheadLog<TKey, TValue> GetWAL<TKey, TValue>(long segmentId, string category)
    {
        if (WALTable.TryGetValue(segmentId + category, out var value))
        {
            return (IWriteAheadLog<TKey, TValue>)value;
        }
        return null;
    }

    public bool RemoveWAL(long segmentId, string category)
    {
        return WALTable.Remove(segmentId + category, out _);
    }

    public void DropStore()
    {
        if (FileStreamProvider.DirectoryExists(WalDirectory))
            FileStreamProvider.DeleteDirectory(WalDirectory, true);
    }

    public void InitCategory(string category)
    {
        var categoryPath = Path.Combine(WalDirectory, category);
        FileStreamProvider.CreateDirectory(categoryPath);
    }
}
