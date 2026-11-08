using Cta.Contracts;
using Cta.Core;
using Cta.Storage;

namespace Cta.Tests.Core;

public sealed class NUnitImportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-008, scope=range_start)
    [Theory]
    [InlineData("nunit3-console.xml", 26)]
    [InlineData("nunit3.xml", 11)]
    public void Sample_report_ends_up_in_the_database(string sample, long expectedResults)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", sample);
        using var file = File.OpenRead(path);
        var sha256 = RunIdentity.ComputeSha256(file);
        var started = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        using var connection = CtaDatabase.Open(Path.Combine(_directory, "analytics.db"));

        var imported = RunImporter.Import(
            connection,
            new RunImport
            {
                Run = new Run
                {
                    RunKey = RunIdentity.CreateLocalRunKey("machine", started),
                    Fingerprint = RunIdentity.ComputeFingerprint([sha256]),
                    Origin = RunOrigin.Local,
                    StartedUtc = started,
                    CommitSha = "abc123",
                    WorkingTreeDirty = false,
                    Duration = TimeSpan.FromSeconds(1),
                },
                Sources =
                [
                    new Source
                    {
                        FileName = sample,
                        Format = ReportFormat.NUnit3,
                        Sha256 = sha256,
                    },
                ],
                Results = NUnitReport.Open(path).ReadResults(),
            }
        );

        Assert.Equal(expectedResults, imported.ResultCount);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SUM(Occurrences) FROM Results;";
        Assert.Equal(expectedResults, command.ExecuteScalar());
    }

    // @relation(CTA-PARSE-008, scope=range_end)
}
