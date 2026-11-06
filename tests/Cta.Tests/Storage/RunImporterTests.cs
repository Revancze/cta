using Cta.Contracts;
using Cta.Storage;
using Microsoft.Data.Sqlite;

namespace Cta.Tests.Storage;

public sealed class RunImporterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    private string DatabasePath => Path.Combine(_directory, "analytics.db");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-IMPORT-001, scope=range_start)
    [Fact]
    public void Import_stores_run_sources_and_results()
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        var result = RunImporter.Import(connection, ImportTestData.Create("run-1", 10));

        Assert.Equal(ImportStatus.Imported, result.Status);
        Assert.Equal(10, result.ResultCount);
        Assert.Equal(1, Count(connection, "Runs"));
        Assert.Equal(1, Count(connection, "Sources"));
        Assert.Equal(10, Count(connection, "TestCases"));
        Assert.Equal(10, Count(connection, "Results"));
    }

    [Fact]
    public void Same_test_and_same_failure_are_stored_once_across_runs()
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        RunImporter.Import(connection, ImportTestData.Create("run-1", 10));
        RunImporter.Import(connection, ImportTestData.Create("run-2", 10));

        Assert.Equal(2, Count(connection, "Runs"));
        Assert.Equal(10, Count(connection, "TestCases"));
        Assert.Equal(1, Count(connection, "Failures"));
        Assert.Equal(20, Count(connection, "Results"));
    }

    // @relation(CTA-IMPORT-001, scope=range_end)

    // @relation(CTA-IMPORT-002, scope=range_start)
    [Fact]
    public void Second_import_of_the_same_run_changes_nothing()
    {
        using var connection = CtaDatabase.Open(DatabasePath);
        var first = RunImporter.Import(connection, ImportTestData.Create("run-1", 10));

        var second = RunImporter.Import(connection, ImportTestData.Create("run-1", 10));

        Assert.Equal(ImportStatus.AlreadyImported, second.Status);
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(1, Count(connection, "Runs"));
        Assert.Equal(10, Count(connection, "Results"));
    }

    // @relation(CTA-IMPORT-002, scope=range_end)

    // @relation(CTA-IMPORT-003, scope=range_start)
    [Fact]
    public void Failed_import_leaves_no_data_behind()
    {
        using var connection = CtaDatabase.Open(DatabasePath);
        var valid = ImportTestData.Create("run-1", 10);
        var broken = new RunImport
        {
            Run = valid.Run,
            Sources = valid.Sources,
            Results = ThrowAfter(valid.Results, 5),
        };

        Assert.Throws<InvalidDataException>(() => RunImporter.Import(connection, broken));

        Assert.Equal(0, Count(connection, "Runs"));
        Assert.Equal(0, Count(connection, "TestCases"));
        Assert.Equal(0, Count(connection, "Results"));
    }

    // @relation(CTA-IMPORT-003, scope=range_end)

    private static IEnumerable<TestResult> ThrowAfter(IEnumerable<TestResult> results, int count)
    {
        foreach (var result in results.Take(count))
        {
            yield return result;
        }

        throw new InvalidDataException("Broken report.");
    }

    private static long Count(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }
}
