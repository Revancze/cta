using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cta.Contracts;
using Microsoft.Data.Sqlite;

namespace Cta.Storage;

/// <summary>
/// Writes one run in a single transaction. Results are consumed as a stream and nothing per test
/// is kept in memory, so memory use does not depend on the size of the report (ADR-0007).
/// </summary>
public static class RunImporter
{
    public static ImportResult Import(SqliteConnection connection, RunImport import)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(import);

        using var transaction = connection.BeginTransaction();

        var existingRunId = FindRun(connection, transaction, import.Run.Fingerprint);
        if (existingRunId is not null)
        {
            return new ImportResult(ImportStatus.AlreadyImported, existingRunId.Value, 0);
        }

        var runId = InsertRun(connection, transaction, import.Run);
        InsertSources(connection, transaction, runId, import.Sources);

        using var writer = new ResultWriter(connection, transaction, runId);
        long count = 0;
        foreach (var result in import.Results)
        {
            writer.Write(result);
            count++;
        }

        transaction.Commit();
        return new ImportResult(ImportStatus.Imported, runId, count);
    }

    private static long? FindRun(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT RunId FROM Runs WHERE Fingerprint = $fingerprint;";
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        return (long?)command.ExecuteScalar();
    }

    private static long InsertRun(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Run run
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Runs (RunKey, Fingerprint, Origin, StartedUtc, Repository, Branch,
                              CommitSha, Os, Framework, Configuration, DurationMs)
            VALUES ($runKey, $fingerprint, $origin, $startedUtc, $repository, $branch,
                    $commitSha, $os, $framework, $configuration, $durationMs)
            RETURNING RunId;
            """;
        command.Parameters.AddWithValue("$runKey", run.RunKey);
        command.Parameters.AddWithValue("$fingerprint", run.Fingerprint);
        command.Parameters.AddWithValue("$origin", ToText(run.Origin));
        command.Parameters.AddWithValue(
            "$startedUtc",
            run.StartedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
        );
        command.Parameters.AddWithValue("$repository", OrNull(run.Repository));
        command.Parameters.AddWithValue("$branch", OrNull(run.Branch));
        command.Parameters.AddWithValue("$commitSha", run.CommitSha);
        command.Parameters.AddWithValue("$os", OrNull(run.Os));
        command.Parameters.AddWithValue("$framework", OrNull(run.Framework));
        command.Parameters.AddWithValue("$configuration", OrNull(run.Configuration));
        command.Parameters.AddWithValue("$durationMs", (long)run.Duration.TotalMilliseconds);
        return (long)command.ExecuteScalar()!;
    }

    private static void InsertSources(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long runId,
        IReadOnlyList<Source> sources
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Sources (RunId, FileName, Format, Sha256)
            VALUES ($runId, $fileName, $format, $sha256);
            """;
        foreach (var source in sources)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$runId", runId);
            command.Parameters.AddWithValue("$fileName", source.FileName);
            command.Parameters.AddWithValue("$format", ToText(source.Format));
            command.Parameters.AddWithValue("$sha256", source.Sha256);
            command.ExecuteNonQuery();
        }
    }

    private static object OrNull(string? value) => (object?)value ?? DBNull.Value;

    private static string ToText(RunOrigin origin) =>
        origin switch
        {
            RunOrigin.Local => "local",
            RunOrigin.Ci => "ci",
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

    private static string ToText(ReportFormat format) =>
        format switch
        {
            ReportFormat.Trx => "trx",
            ReportFormat.NUnit3 => "nunit3",
            ReportFormat.XUnit2 => "xunit2",
            ReportFormat.JUnit => "junit",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static string ToText(TestOutcome outcome) =>
        outcome switch
        {
            TestOutcome.Passed => "Passed",
            TestOutcome.Failed => "Failed",
            TestOutcome.Error => "Error",
            TestOutcome.Skipped => "Skipped",
            TestOutcome.NotRun => "NotRun",
            TestOutcome.Inconclusive => "Inconclusive",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    /// <summary>
    /// Holds the prepared statements, so a large import reuses them instead of parsing the same
    /// SQL for every test.
    /// </summary>
    private sealed class ResultWriter : IDisposable
    {
        private readonly long _runId;
        private readonly SqliteCommand _findTestCase;
        private readonly SqliteCommand _insertTestCase;
        private readonly SqliteCommand _findFailure;
        private readonly SqliteCommand _insertFailure;
        private readonly SqliteCommand _insertResult;

        public ResultWriter(SqliteConnection connection, SqliteTransaction transaction, long runId)
        {
            _runId = runId;
            _findTestCase = Prepare(
                connection,
                transaction,
                "SELECT TestCaseId FROM TestCases WHERE FullName = $fullName;",
                "$fullName"
            );
            _insertTestCase = Prepare(
                connection,
                transaction,
                """
                INSERT INTO TestCases (FullName, Assembly, ClassName, MethodName, FirstSeenRunId)
                VALUES ($fullName, $assembly, $className, $methodName, $runId)
                RETURNING TestCaseId;
                """,
                "$fullName",
                "$assembly",
                "$className",
                "$methodName",
                "$runId"
            );
            _findFailure = Prepare(
                connection,
                transaction,
                "SELECT FailureId FROM Failures WHERE ContentHash = $contentHash;",
                "$contentHash"
            );
            _insertFailure = Prepare(
                connection,
                transaction,
                """
                INSERT INTO Failures (ContentHash, Signature, ExceptionType, Message, StackTrace)
                VALUES ($contentHash, $signature, $exceptionType, $message, $stackTrace)
                RETURNING FailureId;
                """,
                "$contentHash",
                "$signature",
                "$exceptionType",
                "$message",
                "$stackTrace"
            );
            _insertResult = Prepare(
                connection,
                transaction,
                """
                INSERT INTO Results (RunId, TestCaseId, Outcome, DurationMs, FailureId, Output)
                VALUES ($runId, $testCaseId, $outcome, $durationMs, $failureId, $output);
                """,
                "$runId",
                "$testCaseId",
                "$outcome",
                "$durationMs",
                "$failureId",
                "$output"
            );
        }

        public void Write(TestResult result)
        {
            var testCaseId = GetTestCaseId(result.TestCase);
            object failureId = result.Failure is null ? DBNull.Value : GetFailureId(result.Failure);

            var parameters = _insertResult.Parameters;
            parameters["$runId"].Value = _runId;
            parameters["$testCaseId"].Value = testCaseId;
            parameters["$outcome"].Value = ToText(result.Outcome);
            parameters["$durationMs"].Value = (long)result.Duration.TotalMilliseconds;
            parameters["$failureId"].Value = failureId;
            parameters["$output"].Value = OrNull(result.Output);
            _insertResult.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _findTestCase.Dispose();
            _insertTestCase.Dispose();
            _findFailure.Dispose();
            _insertFailure.Dispose();
            _insertResult.Dispose();
        }

        private long GetTestCaseId(TestCase testCase)
        {
            _findTestCase.Parameters["$fullName"].Value = testCase.FullName;
            if (_findTestCase.ExecuteScalar() is long existing)
            {
                return existing;
            }

            var parameters = _insertTestCase.Parameters;
            parameters["$fullName"].Value = testCase.FullName;
            parameters["$assembly"].Value = OrNull(testCase.AssemblyName);
            parameters["$className"].Value = OrNull(testCase.ClassName);
            parameters["$methodName"].Value = OrNull(testCase.MethodName);
            parameters["$runId"].Value = _runId;
            return (long)_insertTestCase.ExecuteScalar()!;
        }

        private long GetFailureId(Failure failure)
        {
            var contentHash = ComputeContentHash(failure);
            _findFailure.Parameters["$contentHash"].Value = contentHash;
            if (_findFailure.ExecuteScalar() is long existing)
            {
                return existing;
            }

            var parameters = _insertFailure.Parameters;
            parameters["$contentHash"].Value = contentHash;
            parameters["$signature"].Value = failure.Signature;
            parameters["$exceptionType"].Value = OrNull(failure.ExceptionType);
            parameters["$message"].Value = OrNull(failure.Message);
            parameters["$stackTrace"].Value = OrNull(failure.StackTrace);
            return (long)_insertFailure.ExecuteScalar()!;
        }

        private static string ComputeContentHash(Failure failure)
        {
            var content = $"{failure.ExceptionType}\0{failure.Message}\0{failure.StackTrace}";
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        }

        private static SqliteCommand Prepare(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            params string[] parameterNames
        )
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var name in parameterNames)
            {
                command.Parameters.Add(
                    new SqliteParameter { ParameterName = name, Value = DBNull.Value }
                );
            }

            command.Prepare();
            return command;
        }
    }
}
