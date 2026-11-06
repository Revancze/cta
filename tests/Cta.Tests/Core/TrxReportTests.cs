using Cta.Contracts;
using Cta.Core;

namespace Cta.Tests.Core;

public sealed class TrxReportTests : IDisposable
{
    private const string ClassName = "Sample.Tests.CalculatorTests";

    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-001, scope=range_start)
    [Theory]
    [InlineData("xunit.trx", 6, 3, 2, 1)]
    [InlineData("nunit.trx", 7, 3, 2, 2)]
    public void Sample_report_is_read_completely(
        string sample,
        int total,
        int passed,
        int failed,
        int skipped
    )
    {
        var results = TrxReport.Open(SamplePath(sample)).ReadResults().ToList();

        Assert.Equal(total, results.Count);
        Assert.Equal(passed, results.Count(r => r.Outcome == TestOutcome.Passed));
        Assert.Equal(failed, results.Count(r => r.Outcome == TestOutcome.Failed));
        Assert.Equal(skipped, results.Count(r => r.Outcome == TestOutcome.Skipped));
    }

    [Theory]
    [InlineData("xunit.trx", "Param(x: 1)")]
    [InlineData("nunit.trx", "Param(1)")]
    public void Full_name_contains_class_and_parameters(string sample, string localName)
    {
        var results = TrxReport.Open(SamplePath(sample)).ReadResults().ToList();

        var result = Assert.Single(results, r => r.TestCase.FullName == $"{ClassName}.{localName}");
        Assert.Equal(ClassName, result.TestCase.ClassName);
        Assert.Equal("Param", result.TestCase.MethodName);
    }

    [Theory]
    [InlineData("xunit.trx")]
    [InlineData("nunit.trx")]
    public void Failure_keeps_exception_message_and_stack_trace(string sample)
    {
        var results = TrxReport.Open(SamplePath(sample)).ReadResults().ToList();

        var failure = results.Single(r => r.TestCase.MethodName == "Throws").Failure;

        Assert.NotNull(failure);
        Assert.Equal("System.InvalidOperationException", failure.ExceptionType);
        Assert.Contains("boom <&> 42", failure.Message, StringComparison.Ordinal);
        Assert.Contains("CalculatorTests.Throws", failure.StackTrace, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("xunit.trx")]
    [InlineData("nunit.trx")]
    public void Skip_reason_and_output_are_kept(string sample)
    {
        var results = TrxReport.Open(SamplePath(sample)).ReadResults().ToList();

        Assert.Equal(
            "not today",
            results.Single(r => r.TestCase.MethodName == "Skipped").Failure?.Message
        );
        Assert.Contains(
            "hello output",
            results.Single(r => r.TestCase.MethodName == "Passes").Output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Report_times_are_read()
    {
        var report = TrxReport.Open(SamplePath("xunit.trx"));

        Assert.NotNull(report.Started);
        Assert.True(report.Finished > report.Started);
    }

    // @relation(CTA-PARSE-001, scope=range_end)

    // @relation(CTA-PARSE-002, scope=range_start)
    [Fact]
    public void Report_with_missing_results_is_refused()
    {
        var path = WriteReport(results: 1, declaredTotal: 2);

        var report = TrxReport.Open(path);

        Assert.Throws<InvalidDataException>(() => report.ReadResults().ToList());
    }

    [Fact]
    public void Report_without_counters_is_refused()
    {
        var path = Path.Combine(_directory, "no-counters.trx");
        File.WriteAllText(path, "<TestRun><Results /></TestRun>");

        Assert.Throws<InvalidDataException>(() => TrxReport.Open(path));
    }

    [Fact]
    public void Unknown_outcome_is_refused()
    {
        var path = WriteReport(results: 1, declaredTotal: 1, outcome: "Maybe");

        var report = TrxReport.Open(path);

        Assert.Throws<InvalidDataException>(() => report.ReadResults().ToList());
    }

    [Fact]
    public void File_that_is_not_a_trx_report_is_refused()
    {
        var path = Path.Combine(_directory, "other.xml");
        File.WriteAllText(path, "<testsuites />");

        Assert.Throws<InvalidDataException>(() => TrxReport.Open(path));
    }

    // @relation(CTA-PARSE-002, scope=range_end)

    // @relation(CTA-PARSE-003, scope=range_start)
    [Fact]
    public void Failure_signature_ignores_volatile_values()
    {
        var first = FailureSignature.Create(
            "System.Exception",
            @"Id 12 in C:\work\a.txt, 01234567-89ab-cdef-0123-456789abcdef",
            "   at Sample.Test() in C:\\work\\Test.cs:line 7"
        );
        var second = FailureSignature.Create(
            "System.Exception",
            "Id 99 in /home/ci/b.txt, fedcba98-7654-3210-fedc-ba9876543210",
            "   at Sample.Test() in /home/ci/Test.cs:line 8"
        );

        Assert.Equal(first, second);
    }

    [Fact]
    public void Failure_signature_distinguishes_different_errors()
    {
        var baseline = FailureSignature.Create("A", "message", "   at Sample.One()");

        Assert.NotEqual(baseline, FailureSignature.Create("B", "message", "   at Sample.One()"));
        Assert.NotEqual(baseline, FailureSignature.Create("A", "other", "   at Sample.One()"));
        Assert.NotEqual(baseline, FailureSignature.Create("A", "message", "   at Sample.Two()"));
    }

    [Fact]
    public void Unknown_stack_trace_format_falls_back_to_type_and_message()
    {
        var python = FailureSignature.Create("ValueError", "bad", "File \"a.py\", line 1");
        var cpp = FailureSignature.Create("ValueError", "bad", "#0 0x00 in main");

        Assert.Equal(python, cpp);
    }

    // @relation(CTA-PARSE-003, scope=range_end)

    private static string SamplePath(string sample) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", sample);

    private string WriteReport(int results, int declaredTotal, string outcome = "Passed")
    {
        var path = Path.Combine(_directory, "report.trx");
        TrxTestFile.Write(path, results, declaredTotal, outcome, outputSize: 0);
        return path;
    }
}
