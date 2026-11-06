using Cta.Core;
using Cta.Tests.Storage;
using Xunit.Abstractions;

namespace Cta.Tests.Core;

[Collection(nameof(PerformanceTestGroup))]
public sealed class TrxReportPerformanceTests(ITestOutputHelper output) : IDisposable
{
    private const long MegaByte = 1024 * 1024;

    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-004, scope=range_start)
    [Fact]
    public void Live_memory_does_not_depend_on_output_size()
    {
        var small = MeasurePeak(results: 10_000, outputSize: 0);
        var large = MeasurePeak(results: 10_000, outputSize: 10_000);
        output.WriteLine(
            $"10k without output: {small / 1024} kB, with 100 MB output: {large / 1024} kB"
        );

        Assert.InRange(large, 0, small + (2 * MegaByte));
    }

    [Fact]
    public void Live_memory_for_100000_results_stays_low()
    {
        var peak = MeasurePeak(results: 100_000, outputSize: 0);
        output.WriteLine($"100k: {peak / 1024} kB");

        Assert.InRange(peak, 0, 32 * MegaByte);
    }

    // @relation(CTA-PARSE-004, scope=range_end)

    private long MeasurePeak(int results, int outputSize)
    {
        var path = Path.Combine(_directory, $"{results}-{outputSize}.trx");
        TrxTestFile.Write(path, results, results, "Passed", outputSize);

        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var report = TrxReport.Open(path);
        var peak = 0L;
        var index = 0;
        foreach (var result in report.ReadResults())
        {
            Assert.NotNull(result);
            if (index++ % 1_000 == 0)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: true));
            }
        }

        GC.KeepAlive(report);
        return peak - baseline;
    }
}
