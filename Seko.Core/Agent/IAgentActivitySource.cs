namespace Seko.Core.Agent;

public interface IAgentActivitySource
{
    event Action<AgentActivity>? ActivityChanged;
}