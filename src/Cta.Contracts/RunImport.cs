namespace Cta.Contracts;

/// <summary>
/// One run to import. Results are a stream so that a large report never has to sit in memory
/// as a whole (ADR-0007).
/// </summary>
public sealed class RunImport
{
    public required Run Run { get; init; }
    public required IReadOnlyList<Source> Sources { get; init; }
    public required IEnumerable<TestResult> Results { get; init; }
}
