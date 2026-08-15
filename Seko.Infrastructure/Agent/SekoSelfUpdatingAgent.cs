using System.Diagnostics;
using Seko.Infrastructure.Diagnostics;
using Seko.Infrastructure.Attachments;
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

        /*
            Self-update tracking is itself a Git workflow. It must not run for
            conversational, read-only, or explicitly non-action requests.

            Only an original request that actually grants workspace-modification
            permission may open the self-update finalization path.
        */
        if (ShouldTrackSelfUpdate(
                conversation)
            && SekoSelfUpdateCoordinator.IsSekoRepository(
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

        /*
            Once the inner agent has returned successfully it may already have
            created a verified local self-update commit. Finalization must not
            be interrupted by a late Stop click, otherwise Seko can be left on
            an old running process with a new commit that was never pushed or
            restarted into.
        */
        if (string.IsNullOrWhiteSpace(
                beforeHead))
        {
            return response;
        }

        var finalizeStartedAt =
            DateTimeOffset.Now;

        var finalizeStopwatch =
            Stopwatch.StartNew();

        var updateResult =
            await SekoSelfUpdateCoordinator.FinalizeAsync(
                _workspace,
                beforeHead,
                ReportActivity,
                CancellationToken.None);

        finalizeStopwatch.Stop();

        _innerAgent.RecordExternalDiagnostic(
            new SekoDiagnosticEvent(
                finalizeStartedAt,
                SekoDiagnosticEventKind.Git,
                "self_update_finalize",
                finalizeStopwatch.Elapsed,
                null,
                updateResult.Message,
                !updateResult.CommitDetected
                || updateResult.PushSucceeded));

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

        var finalResponse =
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

        _innerAgent.RefreshCompletedLog(
            finalResponse.Content);

        return finalResponse;
    }

    public static bool ShouldTrackSelfUpdate(
        IReadOnlyList<ChatMessage> conversation)
    {
        ArgumentNullException.ThrowIfNull(
            conversation);

        var request =
            conversation
                .LastOrDefault(
                    message =>
                        message.Role == MessageRole.User)
                ?.Content
            ?? string.Empty;

        request =
            SekoAttachmentContext.GetUserRequest(
                request);

        var routingDecision =
            SekoRequestRouter.Route(
                request);

        return
            routingDecision.TaskIntent.RequiresModification;
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
        _innerAgent.RecordExternalActivity(
            activity);

        ActivityChanged?.Invoke(
            activity);
    }
}