namespace Seko.Core.Agent;

public interface IRestartAwareAgent
{
    bool RestartRequested { get; }
}