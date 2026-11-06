using System.Diagnostics;
using Cta.Contracts;
using Cta.Storage;

namespace Cta.Tests.Storage;

// Live memory is measured for the whole process, so nothing else may run at the same time.
[CollectionDefinition(nameof(PerformanceTestGroup), DisableParallelization = true)]
public sealed class PerformanceTestGroup;

[Collection(nameof(PerformanceTestGroup))]
public sealed class RunImporterPerformanceTests : IDisposable
{
    private const long MegaByte = 1024 * 1024;

    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-IMPORT-004, scope=range_start)
    [Fact]
    public void Import_of_10000_results_stays_within_time_and_allocation_limits()
    {
        using var connection = CtaDatabase.Open(Path.Combine(_directory, "limits.db"));
        var import = ImportTestData.Create("run-1", 10_000);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        RunImporter.Import(connection, import);
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.InRange(allocated, 0, 50 * MegaByte);
    }

    [Fact]
    public void Live_memory_does_not_grow_with_report_size()
    {
        var peakSmall = MeasurePeakLiveMemory(10_000);
        var peakLarge = MeasurePeakLiveMemory(100_000);

        Assert.InRange(peakLarge, 0, peakSmall * 2);
    }

    // @relation(CTA-IMPORT-004, scope=range_end)

    private long MeasurePeakLiveMemory(int resultCount)
    {
        using var connection = CtaDatabase.Open(Path.Combine(_directory, $"peak-{resultCount}.db"));
        var import = ImportTestData.Create("run-1", resultCount);
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var peak = 0L;
        var sampled = new RunImport
        {
            Run = import.Run,
            Sources = import.Sources,
            Results = SampleLiveMemory(import.Results, value => peak = Math.Max(peak, value)),
        };

        RunImporter.Import(connection, sampled);

        // The baseline keeps whatever the test host already holds out of the comparison.
        return Math.Max(peak - baseline, MegaByte);
    }

    private static IEnumerable<TestResult> SampleLiveMemory(
        IEnumerable<TestResult> results,
        Action<long> onSample
    )
    {
        var index = 0;
        foreach (var result in results)
        {
            if (index++ % 1_000 == 0)
            {
                onSample(GC.GetTotalMemory(forceFullCollection: true));
            }

            yield return result;
        }
    }
}
