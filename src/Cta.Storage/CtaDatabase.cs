using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Cta.Storage;

/// <summary>
/// Opens the CTA database and keeps its schema up to date.
/// </summary>
public static class CtaDatabase
{
    public static int CurrentVersion => Migrations.All.Length;

    /// <summary>
    /// Opens (or creates) the database, enables foreign keys and WAL, and applies pending
    /// migrations. The caller owns the returned connection.
    /// </summary>
    public static SqliteConnection Open(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // No pooling: a closed connection must release the file (Windows locks it otherwise).
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            // Both pragmas are per connection and cannot be set inside a transaction.
            Execute(connection, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;");
            Migrate(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static int GetVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Migrate(SqliteConnection connection)
    {
        var version = GetVersion(connection);
        if (version > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is newer than this CTA supports ({CurrentVersion})."
            );
        }

        for (var next = version + 1; next <= CurrentVersion; next++)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, Migrations.All[next - 1], transaction);
            Execute(
                connection,
                string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {next};"),
                transaction
            );
            transaction.Commit();
        }
    }

    private static void Execute(
        SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction = null
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
