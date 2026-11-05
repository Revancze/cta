namespace Cta.Contracts;

public sealed record Source
{
    public required string FileName { get; init; }
    public required ReportFormat Format { get; init; }
    public required string Sha256 { get; init; }
}
