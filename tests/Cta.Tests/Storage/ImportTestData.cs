using Cta.Contracts;

namespace Cta.Tests.Storage;

internal static class ImportTestData
{
    public static RunImport Create(string key, int resultCount) =>
        new()
        {
            Run = new Run
            {
                RunKey = key,
                Fingerprint = $"fingerprint-{key}",
                Origin = RunOrigin.Local,
                StartedUtc = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
                CommitSha = "abc123",
                Duration = TimeSpan.FromSeconds(1),
            },
            Sources =
            [
                new Source
                {
                    FileName = "report.trx",
                    Format = ReportFormat.Trx,
                    Sha256 = $"sha-{key}",
                },
            ],
            Results = CreateResults(resultCount),
        };

    private static IEnumerable<TestResult> CreateResults(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var failed = i % 10 == 0;
            yield return new TestResult
            {
                TestCase = new TestCase { FullName = $"Namespace.Class.Test{i}" },
                Outcome = failed ? TestOutcome.Failed : TestOutcome.Passed,
                Duration = TimeSpan.FromMilliseconds(i % 100),
                Failure = failed
                    ? new Failure
                    {
                        Signature = "signature",
                        ExceptionType = "Xunit.Sdk.EqualException",
                        Message = "Expected 1, actual 2",
                        StackTrace = "at Namespace.Class.Test()",
                    }
                    : null,
            };
        }
    }
}
