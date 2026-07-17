namespace Warden.Domain.Trust;

public sealed record ActionExecutionResult
{
    public required int ExitCode { get; init; }
    public required string Output { get; init; }
}
