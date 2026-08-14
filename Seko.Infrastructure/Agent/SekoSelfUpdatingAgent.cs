using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class SekoSelfUpdatingAgent :
    IAgent,
    IAgentActivitySource,
    IRestartAwareAgent
{
    private readonly Workspace _workspace;
    private readonly SekoTransactionalAgent _innerAgent;

    public event Action<AgentActivity>? ActivityChanged;

    public bool RestartRequested
    {
        get;
        private set;
    }

    public SekoSelfUpdatingAgent(
        Workspace workspace)
    {
        _workspace =
            workspace;

        var ollamaAgent =
            new OllamaAgent(
                workspace);

        _innerAgent =
            new SekoTransactionalAgent(
                workspace,
                ollamaAgent);

        _innerAgent.ActivityChanged +=
            InnerAgent_ActivityChanged;
    }

    public async Task<ChatMessage> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        RestartRequested =
            false;

        string? beforeHead =
            null;

        if (SekoSelfUpdateCoordinator.IsSekoRepository(
                _workspace))
        {
            beforeHead =
                await SekoSelfUpdateCoordinator.GetHeadAsync(
                    _workspace,
                    cancellationToken);
        }

        var response =
            await _innerAgent.SendAsync(
                conversation,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(
                beforeHead))
        {
            return response;
        }

        var updateResult =
            await SekoSelfUpdateCoordinator.FinalizeAsync(
                _workspace,
                beforeHead,
                ReportActivity,
                cancellationToken);

        if (!updateResult.CommitDetected)
        {
            return response;
        }

        RestartRequested =
            updateResult.ShouldRestart;

        var content =
            response.Content;

        if (!string.IsNullOrWhiteSpace(
                updateResult.Message))
        {
            if (!string.IsNullOrWhiteSpace(
                    content))
            {
                content +=
                    "\n\n";
            }

            content +=
                updateResult.Message;
        }

        return
            new ChatMessage
            {
                Id =
                    response.Id,

                Role =
                    response.Role,

                Content =
                    content,

                CreatedAt =
                    response.CreatedAt
            };
    }

    private void InnerAgent_ActivityChanged(
        AgentActivity activity)
    {
        ActivityChanged?.Invoke(
            activity);
    }

    private void ReportActivity(
        AgentActivity activity)
    {
        ActivityChanged?.Invoke(
            activity);
    }
}