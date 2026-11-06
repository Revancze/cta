using System.Text;
using Cta.Core;

namespace Cta.Tests.Core;

public sealed class RunIdentityTests
{
    // @relation(CTA-ID-001, scope=range_start)
    [Fact]
    public void Report_hash_is_lowercase_sha256_of_its_content()
    {
        using var report = new MemoryStream(Encoding.UTF8.GetBytes("abc"));

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            RunIdentity.ComputeSha256(report)
        );
    }

    [Fact]
    public void Fingerprint_format_never_changes()
    {
        Assert.Equal(
            "7e18f737311b2dc3b2f269dd78396b0351f14fb66efa879f768cb23181883c78",
            RunIdentity.ComputeFingerprint(["a", "b"])
        );
    }

    [Fact]
    public void Fingerprint_does_not_depend_on_source_order()
    {
        Assert.Equal(
            RunIdentity.ComputeFingerprint(["a", "b"]),
            RunIdentity.ComputeFingerprint(["b", "a"])
        );
    }

    [Fact]
    public void Different_sources_give_different_fingerprints()
    {
        Assert.NotEqual(
            RunIdentity.ComputeFingerprint(["a", "b"]),
            RunIdentity.ComputeFingerprint(["a", "c"])
        );
    }

    [Fact]
    public void Fingerprint_needs_at_least_one_source()
    {
        Assert.Throws<ArgumentException>(() => RunIdentity.ComputeFingerprint([]));
    }

    // @relation(CTA-ID-001, scope=range_end)

    // @relation(CTA-ID-002, scope=range_start)
    [Fact]
    public void Local_run_key_contains_machine_and_utc_time()
    {
        var started = new DateTimeOffset(2026, 11, 6, 16, 30, 45, 123, TimeSpan.FromHours(1));

        Assert.Equal(
            "local/PC1/20261106T153045123Z",
            RunIdentity.CreateLocalRunKey("PC1", started)
        );
    }

    [Fact]
    public void Ci_run_key_contains_run_attempt_and_job()
    {
        Assert.Equal("ci/42/2/build", RunIdentity.CreateCiRunKey(42, 2, "build"));
    }

    // @relation(CTA-ID-002, scope=range_end)
}
