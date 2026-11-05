namespace Cta.Contracts;

public sealed record TestCase
{
    public required string FullName { get; init; }
    public string? AssemblyName { get; init; }
    public string? ClassName { get; init; }
    public string? MethodName { get; init; }
}
