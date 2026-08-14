using Seko.Core.Agent;
using Seko.Core.Audit;
using Seko.Core.Chat;
using Seko.Core.Tools;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Agent;

public sealed class DevelopmentAgent : IAgent
{
    private readonly Workspace _workspace;
    private readonly IFileTool _fileTool;
    private readonly IAuditLog _auditLog;

    public DevelopmentAgent(
        Workspace workspace,
        IFileTool fileTool,
        IAuditLog auditLog)
    {
        _workspace = workspace;
        _fileTool = fileTool;
        _auditLog = auditLog;
    }

    public async Task<ChatMessage> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = conversation.LastOrDefault(
            message => message.Role == MessageRole.User);

        if (lastMessage is null)
        {
            return CreateResponse("Hello. I'm Seko.");
        }

        var text = lastMessage.Content.Trim();

        if (text.Equals(
                "create test file",
                StringComparison.OrdinalIgnoreCase))
        {
            await _fileTool.WriteTextAsync(
                _workspace,
                "test.txt",
                "Hello from Seko.",
                cancellationToken);

            return CreateResponse(
                $"Done. I created test.txt inside the '{_workspace.Name}' workspace.");
        }

        if (text.Equals(
                "read test file",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!_fileTool.Exists(_workspace, "test.txt"))
            {
                return CreateResponse(
                    "test.txt does not exist yet.");
            }

            var content = await _fileTool.ReadTextAsync(
                _workspace,
                "test.txt",
                cancellationToken);

            return CreateResponse(
                $"test.txt contains:\n\n{content}");
        }

        if (text.Equals(
                "show activity",
                StringComparison.OrdinalIgnoreCase))
        {
            if (_auditLog.Entries.Count == 0)
            {
                return CreateResponse(
                    "There is no activity recorded yet.");
            }

            var lines = _auditLog.Entries
                .OrderBy(entry => entry.CreatedAt)
                .Select(entry =>
                    $"{entry.CreatedAt:HH:mm:ss} | {entry.Action} | " +
                    $"{(entry.Success ? "Success" : "Failed")} | " +
                    $"{entry.Description}");

            return CreateResponse(
                string.Join(Environment.NewLine, lines));
        }

        if (text.Equals(
                "where is my workspace",
                StringComparison.OrdinalIgnoreCase))
        {
            return CreateResponse(
                $"The current workspace is:\n{_workspace.RootPath}");
        }

        return CreateResponse(
            "I'm still running with my temporary development brain. " +
            "For now try: \"create test file\", \"read test file\", " +
            "\"show activity\", or \"where is my workspace\".");
    }

    private static ChatMessage CreateResponse(string content)
    {
        return new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = content
        };
    }
}