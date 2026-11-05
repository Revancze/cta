namespace Cta.Contracts;

public sealed record Run
{
    public required string RunKey { get; init; }

    public required string Fingerprint { get; init; }

    public required RunOrigin Origin { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required string CommitSha { get; init; }

    public required TimeSpan Duration { get; init; }

    public string? Repository { get; init; }

    public string? Branch { get; init; }

    public string? Os { get; init; }

    public string? Framework { get; init; }

    public string? Configuration { get; init; }
}
