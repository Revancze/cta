using Cta.Contracts;
using Cta.Core;
using Cta.Storage;
using Microsoft.Data.Sqlite;

namespace Cta.Tests.Storage;

public sealed class DuplicateResultsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    private string DatabasePath => Path.Combine(_directory, "analytics.db");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-IMPORT-005, scope=range_start)
    [Theory]
    [InlineData(TestOutcome.Passed, TestOutcome.Failed, "Failed")]
    [InlineData(TestOutcome.Failed, TestOutcome.Passed, "Failed")]
    [InlineData(TestOutcome.Failed, TestOutcome.Error, "Error")]
    [InlineData(TestOutcome.Error, TestOutcome.Failed, "Error")]
    [InlineData(TestOutcome.Passed, TestOutcome.Inconclusive, "Inconclusive")]
    [InlineData(TestOutcome.Skipped, TestOutcome.Passed, "Passed")]
    [InlineData(TestOutcome.NotRun, TestOutcome.Skipped, "Skipped")]
    public void Worse_outcome_wins_in_any_order(
        TestOutcome first,
        TestOutcome second,
        string expected
    )
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        RunImporter.Import(connection, CreateImport(Result(first), Result(second)));

        Assert.Equal(expected, Scalar(connection, "SELECT Outcome FROM Results;"));
    }

    [Fact]
    public void Occurrences_and_duration_are_summed()
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        var imported = RunImporter.Import(
            connection,
            CreateImport(Result(TestOutcome.Passed), Result(TestOutcome.Passed))
        );

        Assert.Equal(2, imported.ResultCount);
        Assert.Equal(2L, Scalar(connection, "SELECT Occurrences FROM Results;"));
        Assert.Equal(20L, Scalar(connection, "SELECT DurationMs FROM Results;"));
    }

    [Fact]
    public void Failure_survives_when_a_passing_duplicate_comes_later()
    {
        using var connection = CtaDatabase.Open(DatabasePath);
        var failed = Result(TestOutcome.Failed) with
        {
            Failure = new Failure { Signature = "signature", Message = "boom" },
        };

        RunImporter.Import(connection, CreateImport(failed, Result(TestOutcome.Passed)));

        Assert.Equal(
            "boom",
            Scalar(connection, "SELECT Message FROM Results JOIN Failures USING (FailureId);")
        );
    }

    [Fact]
    public void Real_report_with_duplicate_names_is_imported_without_losing_count()
    {
        using var connection = CtaDatabase.Open(DatabasePath);
        var report = TrxReport.Open(
            Path.Combine(AppContext.BaseDirectory, "Samples", "xunit-duplicates.trx")
        );

        var imported = RunImporter.Import(
            connection,
            new RunImport
            {
                Run = ImportTestData.Create("run-1", 0).Run,
                Sources = [],
                Results = report.ReadResults(),
            }
        );

        Assert.Equal(10, imported.ResultCount);
        Assert.Equal(8L, Scalar(connection, "SELECT COUNT(*) FROM Results;"));
        Assert.Equal(10L, Scalar(connection, "SELECT SUM(Occurrences) FROM Results;"));
        Assert.Equal(
            "Failed",
            Scalar(
                connection,
                """
                SELECT Outcome FROM Results JOIN TestCases USING (TestCaseId)
                WHERE FullName LIKE '%SameDisplay%';
                """
            )
        );
    }

    // @relation(CTA-IMPORT-005, scope=range_end)

    private static TestResult Result(TestOutcome outcome) =>
        new()
        {
            TestCase = new TestCase { FullName = "Namespace.Class.Test" },
            Outcome = outcome,
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
}
