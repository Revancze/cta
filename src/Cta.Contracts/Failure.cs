namespace Cta.Contracts;

public sealed record Failure
{
    public required string Signature { get; init; }
    public string? ExceptionType { get; init; }
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
}
