namespace Seko.Core.Agent;

public enum AgentActivityKind
{
    Thinking,
    Tool,
    Git,
    Completed,
    Error
}

public sealed record AgentActivity(
    AgentActivityKind Kind,
    string Message);