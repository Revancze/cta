using Cta.Contracts;
using Cta.Core;

namespace Cta.Tests.Core;

public sealed class JUnitReportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-005, scope=range_start)
    [Theory]
    [InlineData("pytest.xml", 13, 6, 3, 2, 2, 0)]
    [InlineData("ctest.xml", 7, 2, 2, 0, 2, 1)]
    [InlineData("dotnet-junit.xml", 10, 6, 3, 0, 1, 0)]
    public void Sample_report_is_read_completely(
        string sample,
        int total,
        int passed,
        int failed,
        int error,
        int skipped,
        int notRun
    )
    {
        var results = Read(sample);

        Assert.Equal(total, results.Count);
        Assert.Equal(passed, results.Count(r => r.Outcome == TestOutcome.Passed));
        Assert.Equal(failed, results.Count(r => r.Outcome == TestOutcome.Failed));
        Assert.Equal(error, results.Count(r => r.Outcome == TestOutcome.Error));
        Assert.Equal(skipped, results.Count(r => r.Outcome == TestOutcome.Skipped));
        Assert.Equal(notRun, results.Count(r => r.Outcome == TestOutcome.NotRun));
    }

    [Theory]
    [InlineData("pytest.xml", "tests.test_sample", "test_param[1]", "test_param")]
    [InlineData("pytest.xml", "tests.test_sample.TestGroup", "test_in_class", "test_in_class")]
    [InlineData("dotnet-junit.xml", "Sample.Tests.CalculatorTests", "Param(x: 1)", "Param")]
    public void Full_name_contains_class_and_parameters(
        string sample,
        string className,
        string localName,
        string methodName
    )
    {
        var result = Assert.Single(
            Read(sample),
            r => r.TestCase.FullName == $"{className}.{localName}"
        );

        Assert.Equal(className, result.TestCase.ClassName);
        Assert.Equal(methodName, result.TestCase.MethodName);
    }

    [Fact]
    public void Test_without_class_keeps_its_own_name()
    {
        var result = Assert.Single(Read("ctest.xml"), r => r.TestCase.FullName == "passes");

        Assert.Null(result.TestCase.ClassName);
        Assert.Equal("passes", result.TestCase.MethodName);
    }

    [Theory]
    [InlineData("pytest.xml", "test_raises", "ValueError", "Žluťoučký kůň <úpěl>", "ValueError")]
    [InlineData(
        "dotnet-junit.xml",
        "Throws",
        "System.InvalidOperationException",
        "boom <&> 42",
        "CalculatorTests.Throws"
    )]
    public void Failure_keeps_exception_message_and_stack_trace(
        string sample,
        string methodName,
        string exceptionType,
        string message,
        string stackTrace
    )
    {
        var failure = Read(sample).Single(r => r.TestCase.MethodName == methodName).Failure;

        Assert.NotNull(failure);
        Assert.Equal(exceptionType, failure.ExceptionType);
        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
        Assert.Contains(stackTrace, failure.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_in_test_setup_is_an_error_not_a_failure()
    {
        var result = Read("pytest.xml").Single(r => r.TestCase.MethodName == "test_setup_error");

        Assert.Equal(TestOutcome.Error, result.Outcome);
        Assert.Contains("fixture exploded", result.Failure?.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pytest.xml", "test_skipped", "not on this machine")]
    [InlineData("ctest.xml", "skipped", "SKIP_REGULAR_EXPRESSION_MATCHED")]
    public void Skip_reason_is_kept(string sample, string methodName, string reason)
    {
        var result = Read(sample).Single(r => r.TestCase.MethodName == methodName);

        Assert.Equal(TestOutcome.Skipped, result.Outcome);
        Assert.Equal(reason, result.Failure?.Message);
    }

    [Fact]
    public void Disabled_test_is_not_reported_as_passed()
    {
        var result = Read("ctest.xml").Single(r => r.TestCase.FullName == "disabled");

        Assert.Equal(TestOutcome.NotRun, result.Outcome);
    }

    [Theory]
    [InlineData("pytest.xml", "test_passes", "hello from stdout")]
    [InlineData("pytest.xml", "test_passes", "hello from stderr")]
    [InlineData("ctest.xml", "passes", "hello from ctest")]
    [InlineData("dotnet-junit.xml", "Passes", "hello output")]
    public void Output_of_the_test_is_kept(string sample, string methodName, string text)
    {
        var result = Read(sample).Single(r => r.TestCase.MethodName == methodName);

        Assert.Contains(text, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Duration_is_read_in_seconds()
    {
        var result = Read("ctest.xml").Single(r => r.TestCase.FullName == "timeout");

        Assert.Equal(TimeSpan.FromSeconds(1.0233), result.Duration);
    }

    [Theory]
    [InlineData("pytest.xml", true)]
    [InlineData("ctest.xml", false)]
    [InlineData("dotnet-junit.xml", false)]
    public void Start_is_known_only_with_a_time_zone(string sample, bool known)
    {
        var report = JUnitReport.Open(SamplePath(sample));

        Assert.Equal(known, report.Started is not null);
    }

    [Theory]
    [InlineData("java.lang.AssertionError", "java.lang.AssertionError")]
    [InlineData("failure", null)]
    public void Exception_type_is_taken_from_the_report_when_it_names_one(
        string type,
        string? expected
    )
    {
        var path = Write(
            $"""
            <testsuite tests="1">
              <testcase name="a"><failure type="{type}" message="expected 1" /></testcase>
            </testsuite>
            """
        );

        var failure = JUnitReport.Open(path).ReadResults().Single().Failure;

        Assert.Equal(expected, failure?.ExceptionType);
        Assert.Equal("expected 1", failure?.Message);
    }

    [Fact]
    public void Nested_suites_are_counted_once()
    {
        var path = Write(
            """
            <testsuites tests="2">
              <testsuite name="outer" tests="2">
                <testsuite name="inner" tests="1"><testcase name="a" /></testsuite>
                <testcase name="b" />
              </testsuite>
            </testsuites>
            """
        );

        Assert.Equal(2, JUnitReport.Open(path).ReadResults().Count());
    }

    // @relation(CTA-PARSE-005, scope=range_end)

    // @relation(CTA-PARSE-006, scope=range_start)
    [Theory]
    [InlineData("""<testsuite tests="2"><testcase name="a" /></testsuite>""")]
    [InlineData(
        """<testsuites tests="3"><testsuite tests="1"><testcase name="a" /></testsuite></testsuites>"""
    )]
    [InlineData(
        """<testsuites failures="2"><testsuite tests="1"><testcase name="a"><failure /></testcase></testsuite></testsuites>"""
    )]
    [InlineData("""<testsuite tests="1" errors="-1"><testcase name="a" /></testsuite>""")]
    [InlineData("""<testsuites><testsuite><testcase name="a" /></testsuite></testsuites>""")]
    [InlineData("""<testsuite tests="many"><testcase name="a" /></testsuite>""")]
    [InlineData("""<testsuite tests="1"><testcase name="a" time="1,5" /></testsuite>""")]
    [InlineData("""<testsuite tests="1"><testcase classname="a" /></testsuite>""")]
    public void Incomplete_or_invalid_report_is_refused(string xml)
    {
        var report = JUnitReport.Open(Write(xml));

        Assert.Throws<InvalidDataException>(() => report.ReadResults().ToList());
    }

    [Theory]
    [InlineData("failures", "<failure />")]
    [InlineData("errors", "<error />")]
    [InlineData("skipped", "<skipped />")]
    [InlineData("disabled", "")]
    public void Declared_number_by_outcome_is_checked(string attribute, string problem)
    {
        var disabled = attribute == "disabled" ? """ status="disabled" """ : " ";
        var matching = Write(
            $"""<testsuite tests="1" {attribute}="1"><testcase name="a"{disabled}>{problem}</testcase></testsuite>""",
            "matching.xml"
        );
        var lying = Write(
            $"""<testsuite tests="1" {attribute}="0"><testcase name="a"{disabled}>{problem}</testcase></testsuite>""",
            "lying.xml"
        );

        Assert.Single(JUnitReport.Open(matching).ReadResults());
        Assert.Throws<InvalidDataException>(() => JUnitReport.Open(lying).ReadResults().ToList());
    }

    [Fact]
    public void Number_the_tool_does_not_write_is_not_checked()
    {
        // The second suite does not say how many tests failed, so the sum is unknown.
        var path = Write(
            """
            <testsuites>
              <testsuite tests="1" failures="0"><testcase name="a" /></testsuite>
              <testsuite tests="1"><testcase name="b"><failure /></testcase></testsuite>
            </testsuites>
            """
        );

        Assert.Equal(2, JUnitReport.Open(path).ReadResults().Count());
    }

    [Fact]
    public void File_that_is_not_a_junit_report_is_refused()
    {
        var path = Write("<TestRun />");

        Assert.Throws<InvalidDataException>(() => JUnitReport.Open(path));
    }

    // @relation(CTA-PARSE-006, scope=range_end)

    private static List<TestResult> Read(string sample) =>
        JUnitReport.Open(SamplePath(sample)).ReadResults().ToList();

    private static string SamplePath(string sample) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", sample);

    private string Write(string xml, string fileName = "report.xml")
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, xml);
        return path;
    }
}
