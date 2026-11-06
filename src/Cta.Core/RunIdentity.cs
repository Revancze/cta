using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cta.Core;

/// <summary>
/// Computes what identifies a run. The fingerprint format must never change: a different result
/// for the same reports would import them again.
/// </summary>
public static class RunIdentity
{
    /// <summary>Hashes a report as a stream, so a large file is not loaded into memory.</summary>
    public static string ComputeSha256(Stream report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Convert.ToHexStringLower(SHA256.HashData(report));
    }

    /// <summary>SHA-256 of the sorted source report hashes, one per line.</summary>
    public static string ComputeFingerprint(IEnumerable<string> sourceHashes)
    {
        ArgumentNullException.ThrowIfNull(sourceHashes);

        var sorted = sourceHashes.Order(StringComparer.Ordinal).ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException("A run needs at least one source.", nameof(sourceHashes));
        }

        var content = Encoding.UTF8.GetBytes(string.Join('\n', sorted));
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    public static string CreateLocalRunKey(string machineName, DateTimeOffset startedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        var timestamp = startedUtc.UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture
        );
        return $"local/{machineName}/{timestamp}";
    }

    public static string CreateCiRunKey(long workflowRunId, int attempt, string job)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job);
        return string.Create(CultureInfo.InvariantCulture, $"ci/{workflowRunId}/{attempt}/{job}");
    }
}
