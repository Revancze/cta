namespace Cta.Contracts;

public sealed record TestResult
{
    public required TestCase TestCase { get; init; }
    public required TestOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public Failure? Failure { get; init; }
    public string? Output { get; init; }
}
