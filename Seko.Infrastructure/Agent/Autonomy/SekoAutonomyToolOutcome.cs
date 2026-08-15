namespace Seko.Infrastructure.Agent;

public enum SekoAutonomyToolOutcomeKind
{
    Success,
    Failure,
    Blocked,
    NoChange
}

public sealed record SekoAutonomyToolOutcome(
    SekoAutonomyToolOutcomeKind Kind,
    string ToolName,
    SekoAutonomySignal? Signal = null,
    string? Detail = null)
{
    public string? ArgumentsJson { get; init; }

    public bool CountsAsMeaningfulProgress =>
        Kind == SekoAutonomyToolOutcomeKind.Success;

    public static SekoAutonomyToolOutcome Success(
        string toolName,
        SekoAutonomySignal? signal = null,
        string? detail = null,
        string? argumentsJson = null)
    {
        return new SekoAutonomyToolOutcome(
            SekoAutonomyToolOutcomeKind.Success,
            toolName,
            signal,
            detail)
        {
            ArgumentsJson =
                argumentsJson
        };
    }

    public static SekoAutonomyToolOutcome Failure(
        string toolName,
        SekoAutonomySignal? signal = null,
        string? detail = null,
        string? argumentsJson = null)
    {
        return new SekoAutonomyToolOutcome(
            SekoAutonomyToolOutcomeKind.Failure,
            toolName,
            signal,
            detail)
        {
            ArgumentsJson =
                argumentsJson
        };
    }

    public static SekoAutonomyToolOutcome Blocked(
        string toolName,
        string? detail = null,
        string? argumentsJson = null)
    {
        return new SekoAutonomyToolOutcome(
            SekoAutonomyToolOutcomeKind.Blocked,
            toolName,
            Detail:
                detail)
        {
            ArgumentsJson =
                argumentsJson
        };
    }

    public static SekoAutonomyToolOutcome NoChange(
        string toolName,
        string? detail = null,
        string? argumentsJson = null)
    {
        return new SekoAutonomyToolOutcome(
            SekoAutonomyToolOutcomeKind.NoChange,
            toolName,
            Detail:
                detail)
        {
            ArgumentsJson =
                argumentsJson
        };
    }
}