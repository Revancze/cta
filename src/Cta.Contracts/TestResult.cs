namespace Cta.Contracts;

public sealed record TestResult
{
    public required TestCase TestCase { get; init; }
    public required TestOutcome Outcome { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// What the report says beyond the outcome, for example Timeout or Warning. Null when
    /// the report says nothing more. Analyses count with <see cref="Outcome"/>.
    /// </summary>
    public string? OutcomeDetail { get; init; }

    public Failure? Failure { get; init; }
    public string? Output { get; init; }
}
