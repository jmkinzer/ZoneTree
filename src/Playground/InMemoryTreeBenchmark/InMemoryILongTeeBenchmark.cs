using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;

namespace Playground.InMemoryTreeBenchmark;

[HtmlExporter]
[SimpleJob(RunStrategy.ColdStart, targetCount: 1)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn, /*AllStatisticsColumn*/]
[RPlotExporter]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[HardwareCounters(
    HardwareCounter.TotalCycles,
    HardwareCounter.TotalIssues,
    HardwareCounter.CacheMisses,
    HardwareCounter.Timer)]
public class InMemoryLongTreeBenchmark
{
    readonly int Count = 1_000_000;
    readonly bool Shuffled = true;

    [GlobalSetup]
    public void Setup()
    {
        Data = Shuffled ?
            RandomLongInserts.GetRandomArray(Count) :
            RandomLongInserts.GetSortedArray(Count);
    }

    long[] Data = Array.Empty<long>();

    [Benchmark]
    public void InsertBplusTree() => RandomLongInserts.InsertBplusTree(Data);

    [Benchmark]
    public void InsertSkipList() => RandomLongInserts.InsertSkipList(Data);

    [Benchmark]
    public void InsertSortedDictionary() => RandomLongInserts.InsertSortedDictionary(Data);

}