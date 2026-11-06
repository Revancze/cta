using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cta.Core;

/// <summary>
/// Groups the same failure across runs: exception type, message without volatile values and the
/// first stack frame. A stack trace in an unknown format simply adds no frame (ADR-0009).
/// </summary>
public static partial class FailureSignature
{
    public static string Create(string? exceptionType, string? message, string? stackTrace)
    {
        var content = string.Join(
            '\n',
            exceptionType ?? string.Empty,
            Normalize(message),
            FirstFrame(stackTrace)
        );
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var normalized = GuidPattern().Replace(message, "<guid>");
        normalized = PathPattern().Replace(normalized, "<path>");
        normalized = NumberPattern().Replace(normalized, "<n>");
        return WhitespacePattern().Replace(normalized, " ").Trim();
    }

    private static string FirstFrame(string? stackTrace)
    {
        var match = stackTrace is null ? Match.Empty : FramePattern().Match(stackTrace);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"([A-Za-z]:\\|/)[^\s""']*[\\/][^\s""']*")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^\s*at (.+?)(?: in .*)?\s*$", RegexOptions.Multiline)]
    private static partial Regex FramePattern();
}
