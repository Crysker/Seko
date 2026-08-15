using System.Text.Json.Nodes;
using Seko.Core.Chat;
using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class FastPathRouterRegressionTests
{
    [Fact]
    public void Router_ShishaQuestionUsesFastConversation()
    {
        var decision =
            SekoRequestRouter.Route(
                "How do I make a good shisha head?");

        Assert.True(
            decision.UseFastConversation);

        Assert.False(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
            decision.RequiresWebResearch);
    }

    [Fact]
    public void Router_AbstractClassQuestionUsesFastConversation()
    {
        var decision =
            SekoRequestRouter.Route(
                "Explain the difference between an abstract class and an interface in C# and when I should use each.");

        Assert.True(
            decision.UseFastConversation);

        Assert.False(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
            decision.RequiresWebResearch);
    }

    [Fact]
    public void Router_CurrentFactKeepsAgentResearchPath()
    {
        var decision =
            SekoRequestRouter.Route(
                "What is the latest stable .NET release from official sources?");

        Assert.False(
            decision.UseFastConversation);

        Assert.True(
            decision.RequiresWebResearch);
    }

    [Fact]
    public void Router_WorkspaceModificationKeepsAgentPath()
    {
        var decision =
            SekoRequestRouter.Route(
                "Fix the Stop button in Seko and build the solution.");

        Assert.False(
            decision.UseFastConversation);

        Assert.True(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.True(
            decision.TaskIntent.RequiresModification);
    }

    [Fact]
    public void Router_ExplicitDeepAnalysisKeepsAgentPath()
    {
        var decision =
            SekoRequestRouter.Route(
                "Give me a comprehensive analysis of the philosophical differences between stoicism and existentialism.");

        Assert.False(
            decision.UseFastConversation);

        Assert.False(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
            decision.RequiresWebResearch);
    }

    [Fact]
    public void FastConversationPromptDoesNotExposeAgentMachinery()
    {
        var messages =
            SekoFastConversation.BuildMessages(
                new[]
                {
                    new ChatMessage
                    {
                        Role = MessageRole.User,
                        Content = "Explain interfaces simply."
                    }
                });

        var systemPrompt =
            messages[0]!
                .AsObject()["content"]!
                .GetValue<string>();

        Assert.False(
            systemPrompt.Contains(
                "ACTIVE WORKSPACE",
                StringComparison.OrdinalIgnoreCase));

        Assert.False(
            systemPrompt.Contains(
                "web_research",
                StringComparison.OrdinalIgnoreCase));

        Assert.False(
            systemPrompt.Contains(
                "git_status",
                StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            "This fast conversation path has no tools.",
            systemPrompt);
    }

    [Fact]
    public void FastConversationRequestHasSmallBudgetAndNoTools()
    {
        var messages =
            new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "Hello"
                }
            };

        var request =
            SekoFastConversation.CreateRequest(
                "qwen3:8b",
                messages);

        Assert.False(
            request.ContainsKey(
                "tools"));

        Assert.False(
            request.ContainsKey(
                "tool_choice"));

        Assert.False(
            request["think"]!
                .GetValue<bool>());

        var options =
            request["options"]!
                .AsObject();

        Assert.Equal(
            4096,
            options["num_ctx"]!
                .GetValue<int>());

        Assert.Equal(
            768,
            options["num_predict"]!
                .GetValue<int>());
    }
}
