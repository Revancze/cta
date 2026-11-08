using Cta.Contracts;
using Cta.Core;

namespace Cta.Tests.Core;

public sealed class NUnitReportTests : IDisposable
{
    private const string Console = "nunit3-console.xml";
    private const string Logger = "nunit3.xml";

    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-008, scope=range_start)
    [Theory]
    [InlineData(Console, 26, 11, 4, 6, 3, 2)]
    [InlineData(Logger, 11, 6, 3, 0, 1, 1)]
    public void Sample_report_is_read_completely(
        string sample,
        int total,
        int passed,
        int failed,
        int error,
        int skipped,
        int inconclusive
    )
    {
        var results = Read(sample);

        Assert.Equal(total, results.Count);
        Assert.Equal(passed, results.Count(r => r.Outcome == TestOutcome.Passed));
        Assert.Equal(failed, results.Count(r => r.Outcome == TestOutcome.Failed));
        Assert.Equal(error, results.Count(r => r.Outcome == TestOutcome.Error));
        Assert.Equal(skipped, results.Count(r => r.Outcome == TestOutcome.Skipped));
        Assert.Equal(inconclusive, results.Count(r => r.Outcome == TestOutcome.Inconclusive));
    }

    [Theory]
    [InlineData(
        Console,
        "Sample.Tests.ParamFixture(1).InFixture",
        "Sample.Tests.ParamFixture",
        "InFixture"
    )]
    [InlineData(Console, "Sample.Tests.Outer+Inner.Nested", "Sample.Tests.Outer+Inner", "Nested")]
    [InlineData(
        Console,
        "Sample.Tests.CalculatorTests.Param(1)",
        "Sample.Tests.CalculatorTests",
        "Param"
    )]
    [InlineData(Logger, "Sample.Tests.CalculatorTests.Param(1)", "CalculatorTests", "Param")]
    public void Full_name_is_taken_from_the_report(
        string sample,
        string fullName,
        string className,
        string methodName
    )
    {
        var result = Assert.Single(Read(sample), r => r.TestCase.FullName == fullName);

        Assert.Equal(className, result.TestCase.ClassName);
        Assert.Equal(methodName, result.TestCase.MethodName);
    }

    [Fact]
    public void Same_test_has_the_same_name_as_in_trx()
    {
        var names = Read(Console).Select(r => r.TestCase.FullName).ToHashSet();
        var trx = TrxReport.Open(SamplePath("nunit.trx")).ReadResults();

        Assert.All(trx, r => Assert.Contains(r.TestCase.FullName, names));
    }

    [Fact]
    public void Assembly_of_the_test_is_kept()
    {
        Assert.All(Read(Console), r => Assert.Equal("SampleNunit.dll", r.TestCase.AssemblyName));
    }

    [Theory]
    [InlineData("Sample.Tests.CalculatorTests.Fails", TestOutcome.Failed)]
    [InlineData("Sample.Tests.CalculatorTests.Throws", TestOutcome.Error)]
    [InlineData("Sample.Tests.BrokenSetup.NeverRuns", TestOutcome.Error)]
    [InlineData("Sample.Tests.BrokenFixture.A", TestOutcome.Error)]
    [InlineData("Sample.Tests.Invalid.HasParameter", TestOutcome.Error)]
    [InlineData("Sample.Tests.MoreStates.Warns", TestOutcome.Passed)]
    [InlineData("Sample.Tests.MoreStates.AssumeFails", TestOutcome.Inconclusive)]
    [InlineData("Sample.Tests.MoreStates.OnlyByHand", TestOutcome.Skipped)]
    [InlineData("Sample.Tests.IgnoredFixture.C", TestOutcome.Skipped)]
    public void Result_and_label_give_the_outcome(string fullName, TestOutcome expected)
    {
        var result = Assert.Single(Read(Console), r => r.TestCase.FullName == fullName);

        Assert.Equal(expected, result.Outcome);
    }

    [Theory]
    [InlineData("Sample.Tests.MoreStates.Warns", "careful")]
    [InlineData("Sample.Tests.MoreStates.OnlyByHand", "only by hand")]
    [InlineData("Sample.Tests.CalculatorTests.Unclear", "maybe")]
    public void Reason_of_a_result_without_failure_is_kept(string fullName, string reason)
    {
        var result = Assert.Single(Read(Console), r => r.TestCase.FullName == fullName);

        Assert.Equal(reason, result.Failure?.Message);
    }

    [Theory]
    [InlineData("Sample.Tests.CalculatorTests.Throws", "boom <&> 42", "CalculatorTests.Throws")]
    [InlineData("Sample.Tests.BrokenFixture.A", "fixture exploded", "BrokenFixture.Once")]
    [InlineData(
        "Sample.Tests.BrokenTeardown.RunsThenBreaks",
        "teardown exploded",
        "BrokenTeardown.TearDown"
    )]
    public void Failure_keeps_exception_message_and_stack_trace(
        string fullName,
        string message,
        string stackTrace
    )
    {
        var failure = Assert.Single(Read(Console), r => r.TestCase.FullName == fullName).Failure;

        Assert.NotNull(failure);
        Assert.Equal("System.InvalidOperationException", failure.ExceptionType);
        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
        Assert.Contains(stackTrace, failure.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void Assertions_do_not_overwrite_the_failure()
    {
        var failure = Assert
            .Single(Read(Console), r => r.TestCase.FullName == "Sample.Tests.MoreStates.Multiple")
            .Failure;

        Assert.StartsWith("Multiple failures", failure?.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sample.Tests.CalculatorTests.Passes", "hello output")]
    [InlineData("Sample.Tests.MoreStates.Prints", "stdout line")]
    public void Output_of_the_test_is_kept(string fullName, string text)
    {
        var result = Assert.Single(Read(Console), r => r.TestCase.FullName == fullName);

        Assert.Contains(text, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Console)]
    [InlineData(Logger)]
    public void Report_times_are_read(string sample)
    {
        var report = NUnitReport.Open(SamplePath(sample));

        Assert.NotNull(report.Started);
        Assert.True(report.Finished > report.Started);
    }

    [Fact]
    public void Failed_with_unknown_label_stays_failed()
    {
        var path = Write(
            """<test-run total="1"><test-case fullname="a" result="Failed" label="New" /></test-run>"""
        );

        Assert.Equal(TestOutcome.Failed, NUnitReport.Open(path).ReadResults().Single().Outcome);
    }

    // @relation(CTA-PARSE-008, scope=range_end)

    // @relation(CTA-PARSE-009, scope=range_start)
    [Theory]
    [InlineData("""<test-run total="2"><test-case fullname="a" result="Passed" /></test-run>""")]
    [InlineData("""<test-run total="1"><test-case fullname="a" result="Maybe" /></test-run>""")]
    [InlineData("""<test-run total="1"><test-case result="Passed" /></test-run>""")]
    [InlineData(
        """<test-run total="1"><test-case fullname="a" result="Passed" duration="x" /></test-run>"""
    )]
    public void Incomplete_or_invalid_report_is_refused(string xml)
    {
        var report = NUnitReport.Open(Write(xml));

        Assert.Throws<InvalidDataException>(() => report.ReadResults().ToList());
    }

    [Theory]
    [InlineData("passed", "Passed")]
    [InlineData("failed", "Failed")]
    [InlineData("warnings", "Warning")]
    [InlineData("inconclusive", "Inconclusive")]
    [InlineData("skipped", "Skipped")]
    public void Declared_number_by_result_is_checked(string attribute, string result)
    {
        var matching = Write(
            $"""<test-run total="1" {attribute}="1"><test-case fullname="a" result="{result}" /></test-run>""",
            "matching.xml"
        );
        var lying = Write(
            $"""<test-run total="1" {attribute}="0"><test-case fullname="a" result="{result}" /></test-run>""",
            "lying.xml"
        );

        Assert.Single(NUnitReport.Open(matching).ReadResults());
        Assert.Throws<InvalidDataException>(() => NUnitReport.Open(lying).ReadResults().ToList());
    }

    [Theory]
    [InlineData("<test-run />")]
    [InlineData("""<test-run total="many" />""")]
    [InlineData("<testsuites />")]
    public void Report_without_a_valid_total_is_refused(string xml)
    {
        var path = Write(xml);

        Assert.Throws<InvalidDataException>(() => NUnitReport.Open(path));
    }

    // @relation(CTA-PARSE-009, scope=range_end)

    private static List<TestResult> Read(string sample) =>
        NUnitReport.Open(SamplePath(sample)).ReadResults().ToList();

    private static string SamplePath(string sample) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", sample);

    private string Write(string xml, string fileName = "report.xml")
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, xml);
        return path;
    }
}
