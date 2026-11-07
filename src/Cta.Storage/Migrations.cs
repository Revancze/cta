namespace Cta.Storage;

/// <summary>
/// Schema migrations. Migration N (counted from 1) brings the database to
/// <c>PRAGMA user_version</c> N. Only append new migrations; never edit a released one.
/// </summary>
internal static class Migrations
{
    public static readonly string[] All =
    [
        """
            CREATE TABLE Runs (
                RunId          INTEGER PRIMARY KEY,
                RunKey         TEXT NOT NULL UNIQUE,
                Fingerprint    TEXT NOT NULL UNIQUE,
                Origin         TEXT NOT NULL CHECK (Origin IN ('local', 'ci')),
                StartedUtc     TEXT NOT NULL,
                Repository     TEXT,
                Branch         TEXT,
                CommitSha      TEXT NOT NULL,
                WorkingTreeDirty INTEGER NOT NULL CHECK (WorkingTreeDirty IN (0, 1)),
                Os             TEXT,
                Framework      TEXT,
                Configuration  TEXT,
                DurationMs     INTEGER NOT NULL
            );

            CREATE TABLE TestCases (
                TestCaseId     INTEGER PRIMARY KEY,
                FullName       TEXT NOT NULL UNIQUE,
                Assembly       TEXT,
                ClassName      TEXT,
                MethodName     TEXT,
                FirstSeenRunId INTEGER REFERENCES Runs(RunId)
            );

            CREATE TABLE Failures (
                FailureId      INTEGER PRIMARY KEY,
                ContentHash    TEXT NOT NULL UNIQUE,
                Signature      TEXT NOT NULL,
                ExceptionType  TEXT,
                Message        TEXT,
                StackTrace     TEXT
            );

            CREATE TABLE Results (
                RunId          INTEGER NOT NULL REFERENCES Runs(RunId) ON DELETE CASCADE,
                TestCaseId     INTEGER NOT NULL REFERENCES TestCases(TestCaseId),
                Outcome        TEXT NOT NULL CHECK (Outcome IN
                               ('Passed', 'Failed', 'Error', 'Skipped', 'NotRun', 'Inconclusive')),
                DurationMs     INTEGER NOT NULL,
                Occurrences    INTEGER NOT NULL DEFAULT 1,
                FailureId      INTEGER REFERENCES Failures(FailureId),
                Output         TEXT,
                PRIMARY KEY (RunId, TestCaseId)
            ) WITHOUT ROWID;

            CREATE TABLE Sources (
                RunId          INTEGER NOT NULL REFERENCES Runs(RunId) ON DELETE CASCADE,
                FileName       TEXT NOT NULL,
                Format         TEXT NOT NULL,
                Sha256         TEXT NOT NULL,
                PRIMARY KEY (RunId, Sha256)
            );

            CREATE INDEX IX_Results_TestCase ON Results(TestCaseId, RunId);
            CREATE INDEX IX_Runs_Branch_Time ON Runs(Branch, StartedUtc);
            CREATE INDEX IX_Runs_Commit ON Runs(CommitSha);
            CREATE INDEX IX_Failures_Signature ON Failures(Signature);
            """,
    ];
}
