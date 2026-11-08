using Cta.Contracts;
using Cta.Core;
using Cta.Storage;
using Cta.Tests.Storage;
using Microsoft.Data.Sqlite;

namespace Cta.Tests.Core;

public sealed class OutcomeDetailTests : IDisposable
{
    private const string Console = "nunit3-console.xml";

    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    private string DatabasePath => Path.Combine(_directory, "analytics.db");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-010, scope=range_start)
    [Theory]
    [InlineData("Sample.Tests.MoreStates.Warns", "Warning")]
    [InlineData("Sample.Tests.MoreStates.TimesOut", "Timeout")]
    [InlineData("Sample.Tests.Invalid.HasParameter", "Invalid")]
    [InlineData("Sample.Tests.MoreStates.OnlyByHand", "Explicit")]
    [InlineData("Sample.Tests.CalculatorTests.Skipped", "Ignored")]
    [InlineData("Sample.Tests.IgnoredFixture.C", "Ignored")]
    [InlineData("Sample.Tests.BrokenFixture.A", "Parent")]
    [InlineData("Sample.Tests.BrokenTeardown.RunsThenBreaks", "Teardown")]
    [InlineData("Sample.Tests.CalculatorTests.Throws", null)]
    [InlineData("Sample.Tests.CalculatorTests.Fails", null)]
    [InlineData("Sample.Tests.CalculatorTests.Passes", null)]
    public void NUnit_detail_comes_from_the_report(string fullName, string? expected)
    {
        var result = Assert.Single(
            NUnitReport.Open(SamplePath(Console)).ReadResults(),
            r => r.TestCase.FullName == fullName
        );

        Assert.Equal(expected, result.OutcomeDetail);
    }

    [Fact]
    public void Unknown_NUnit_label_is_kept()
    {
        var path = Write(
            """<test-run total="1"><test-case fullname="a" result="Failed" label="New" /></test-run>"""
        );

        Assert.Equal("New", NUnitReport.Open(path).ReadResults().Single().OutcomeDetail);
    }

    [Theory]
    [InlineData("pytest.xml", "tests.test_sample.test_xfail", "xfail")]
    [InlineData("pytest.xml", "tests.test_sample.test_setup_error", "Setup")]
    [InlineData("pytest.xml", "tests.test_sample.test_teardown_error", "Teardown")]
    [InlineData("pytest.xml", "tests.test_sample.test_skipped", null)]
    [InlineData("pytest.xml", "tests.test_sample.test_fails", null)]
    [InlineData("ctest.xml", "timeout", "Timeout")]
    [InlineData("ctest.xml", "fails", null)]
    public void JUnit_detail_comes_from_the_report(string sample, string fullName, string? expected)
    {
        var result = Assert.Single(
            JUnitReport.Open(SamplePath(sample)).ReadResults(),
            r => r.TestCase.FullName == fullName
        );

        Assert.Equal(expected, result.OutcomeDetail);
    }

    [Fact]
    public void Message_mentioning_a_timeout_is_not_a_timeout()
    {
        var path = Write(
            """
            <testsuite tests="1">
              <testcase name="a"><failure message="Timeout" /></testcase>
            </testsuite>
            """
        );

        Assert.Null(JUnitReport.Open(path).ReadResults().Single().OutcomeDetail);
    }

    [Theory]
    [InlineData("Timeout", "Timeout")]
    [InlineData("Aborted", "Aborted")]
    [InlineData("Disconnected", "Disconnected")]
    [InlineData("Failed", null)]
    [InlineData("Passed", null)]
    public void Trx_detail_keeps_what_the_outcome_loses(string outcome, string? expected)
    {
        var path = Path.Combine(_directory, "report.trx");
        TrxTestFile.Write(path, results: 1, declaredTotal: 1, outcome, outputSize: 0);

        Assert.Equal(expected, TrxReport.Open(path).ReadResults().Single().OutcomeDetail);
    }

    [Fact]
    public void Detail_is_stored_with_the_result()
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        RunImporter.Import(connection, CreateImport(Result(TestOutcome.Passed, "Warning")));

        Assert.Equal("Warning", Scalar(connection, "SELECT OutcomeDetail FROM Results;"));
    }

    [Theory]
    [InlineData(TestOutcome.Failed, "Timeout", TestOutcome.Passed, "Warning", "Timeout")]
    [InlineData(TestOutcome.Passed, "Warning", TestOutcome.Failed, "Timeout", "Timeout")]
    [InlineData(TestOutcome.Passed, "Warning", TestOutcome.Failed, null, null)]
    [InlineData(TestOutcome.Failed, null, TestOutcome.Passed, "Warning", null)]
    [InlineData(TestOutcome.Failed, null, TestOutcome.Failed, "Timeout", "Timeout")]
    [InlineData(TestOutcome.Failed, "Timeout", TestOutcome.Failed, null, "Timeout")]
    public void Merged_result_keeps_the_detail_of_the_winner(
        TestOutcome first,
        string? firstDetail,
        TestOutcome second,
        string? secondDetail,
        string? expected
    )
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        RunImporter.Import(
            connection,
            CreateImport(Result(first, firstDetail), Result(second, secondDetail))
        );

        Assert.Equal(
            expected ?? (object)DBNull.Value,
            Scalar(connection, "SELECT OutcomeDetail FROM Results;")
        );
    }

    // @relation(CTA-PARSE-010, scope=range_end)

    // @relation(CTA-DB-006, scope=range_start)
    [Fact]
    public void Database_from_the_previous_version_keeps_its_data()
    {
        using (var connection = CtaDatabase.Open(DatabasePath))
        {
            RunImporter.Import(connection, CreateImport(Result(TestOutcome.Failed, null)));
            Execute(
                connection,
                "ALTER TABLE Results DROP COLUMN OutcomeDetail; PRAGMA user_version = 1;"
            );
        }

        using var upgraded = CtaDatabase.Open(DatabasePath);

        Assert.Equal(CtaDatabase.CurrentVersion, CtaDatabase.GetVersion(upgraded));
        Assert.Equal(1L, Scalar(upgraded, "SELECT COUNT(*) FROM Runs;"));
        Assert.Equal("Failed", Scalar(upgraded, "SELECT Outcome FROM Results;"));
        Assert.Equal(DBNull.Value, Scalar(upgraded, "SELECT OutcomeDetail FROM Results;"));
    }

    // @relation(CTA-DB-006, scope=range_end)

    private static TestResult Result(TestOutcome outcome, string? detail) =>
        new()
        {
            TestCase = new TestCase { FullName = "Namespace.Class.Test" },
            Outcome = outcome,
            OutcomeDetail = detail,
            Duration = TimeSpan.FromMilliseconds(10),
        };

    private static RunImport CreateImport(params TestResult[] results) =>
        new()
        {
            Run = ImportTestData.Create("run-1", 0).Run,
            Sources = [],
            Results = results,
        };

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string SamplePath(string sample) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", sample);

    private string Write(string xml)
    {
        var path = Path.Combine(_directory, "report.xml");
        File.WriteAllText(path, xml);
        return path;
    }
}
