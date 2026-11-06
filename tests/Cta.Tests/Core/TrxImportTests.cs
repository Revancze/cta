using Cta.Contracts;
using Cta.Core;
using Cta.Storage;

namespace Cta.Tests.Core;

public sealed class TrxImportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-001, scope=range_start)
    [Theory]
    [InlineData("xunit.trx", 6)]
    [InlineData("nunit.trx", 7)]
    public void Sample_report_ends_up_in_the_database(string sample, long expectedResults)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", sample);
        using var file = File.OpenRead(path);
        var sha256 = RunIdentity.ComputeSha256(file);
        var report = TrxReport.Open(path);
        using var connection = CtaDatabase.Open(Path.Combine(_directory, "analytics.db"));

        var imported = RunImporter.Import(
            connection,
            new RunImport
            {
                Run = new Run
                {
                    RunKey = RunIdentity.CreateLocalRunKey("machine", report.Started!.Value),
                    Fingerprint = RunIdentity.ComputeFingerprint([sha256]),
                    Origin = RunOrigin.Local,
                    StartedUtc = report.Started.Value,
                    CommitSha = "abc123",
                    WorkingTreeDirty = false,
                    Duration = report.Finished!.Value - report.Started.Value,
                },
                Sources =
                [
                    new Source
                    {
                        FileName = sample,
                        Format = ReportFormat.Trx,
                        Sha256 = sha256,
                    },
                ],
                Results = report.ReadResults(),
            }
        );

        Assert.Equal(expectedResults, imported.ResultCount);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Results;";
        Assert.Equal(expectedResults, command.ExecuteScalar());
    }

    // @relation(CTA-PARSE-001, scope=range_end)
}
