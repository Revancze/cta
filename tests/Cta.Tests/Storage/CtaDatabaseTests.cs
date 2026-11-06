using Cta.Storage;
using Microsoft.Data.Sqlite;

namespace Cta.Tests.Storage;

public sealed class CtaDatabaseTests : IDisposable
{
    private const int SqliteConstraintError = 19;

    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    private string DatabasePath => Path.Combine(_directory, "analytics.db");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-DB-001, scope=range_start)
    [Fact]
    public void Open_creates_database_with_current_schema()
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        Assert.Equal(CtaDatabase.CurrentVersion, CtaDatabase.GetVersion(connection));
        Assert.Equal(
            ["Failures", "Results", "Runs", "Sources", "TestCases"],
            Query(connection, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;")
        );
    }

    [Fact]
    public void Reopening_keeps_schema_version()
    {
        using (CtaDatabase.Open(DatabasePath)) { }

        using var connection = CtaDatabase.Open(DatabasePath);

        Assert.Equal(CtaDatabase.CurrentVersion, CtaDatabase.GetVersion(connection));
    }

    // @relation(CTA-DB-001, scope=range_end)

    // @relation(CTA-DB-002, scope=range_start)
    [Fact]
    public void Foreign_keys_are_enforced_on_every_connection()
    {
        using (CtaDatabase.Open(DatabasePath)) { }

        using var connection = CtaDatabase.Open(DatabasePath);
        var exception = Assert.Throws<SqliteException>(() =>
            Query(
                connection,
                "INSERT INTO Results (RunId, TestCaseId, Outcome, DurationMs) VALUES (1, 1, 'Passed', 0);"
            )
        );

        Assert.Equal(SqliteConstraintError, exception.SqliteErrorCode);
    }

    // @relation(CTA-DB-002, scope=range_end)

    // @relation(CTA-DB-003, scope=range_start)
    [Fact]
    public void Database_uses_write_ahead_log()
    {
        using var connection = CtaDatabase.Open(DatabasePath);

        Assert.Equal(["wal"], Query(connection, "PRAGMA journal_mode;"));
    }

    // @relation(CTA-DB-003, scope=range_end)

    // @relation(CTA-DB-004, scope=range_start)
    [Fact]
    public void Database_from_newer_version_is_refused()
    {
        using (var raw = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            raw.Open();
            Query(raw, $"PRAGMA user_version = {CtaDatabase.CurrentVersion + 1};");
        }

        Assert.Throws<InvalidOperationException>(() => CtaDatabase.Open(DatabasePath));
    }

    // @relation(CTA-DB-004, scope=range_end)

    // @relation(CTA-DB-005, scope=range_start)
    [Fact]
    public void Closed_connection_releases_database_file()
    {
        using (CtaDatabase.Open(DatabasePath)) { }

        File.Delete(DatabasePath);

        Assert.False(File.Exists(DatabasePath));
    }

    // @relation(CTA-DB-005, scope=range_end)

    private static List<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
