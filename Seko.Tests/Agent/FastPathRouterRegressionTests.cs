using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    public void Router_CurrentCSharpVersionKeepsResearchPath()
    {
        var decision =
            SekoRequestRouter.Route(
                "What is the current C# language version?");

        Assert.False(
            decision.UseFastConversation);

        Assert.True(
            decision.RequiresWebResearch);
    }

    [Fact]
    public void Router_ModernCSharpConceptQuestionStaysFast()
    {
        var decision =
            SekoRequestRouter.Route(
                "Can modern C# interfaces provide default method implementations?");

        Assert.True(
            decision.UseFastConversation);

        Assert.False(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
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

    [Theory]
    [InlineData(
        "hey so can you read your own code and improve/add more features to it? Just a question, not asking you to do it.")]
    [InlineData(
        "Can you modify your own source? Just a question.")]
    [InlineData(
        "Could you add a feature to Seko? Don't actually do it.")]
    [InlineData(
        "Are you able to refactor this repo? No action needed.")]
    [InlineData(
        "Take your time: can you improve your own code? Just a question.")]
    public void Router_ExplicitNonActionCapabilityQuestionSuppressesExecution(
        string request)
    {
        var decision =
            SekoRequestRouter.Route(
                request);

        Assert.True(
            decision.UseFastConversation);

        Assert.True(
            decision.TaskIntent.ExecutionSuppressed);

        Assert.False(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
            decision.TaskIntent.RequiresModification);

        Assert.False(
            decision.RequiresWebResearch);
    }

    [Fact]
    public void Router_CapabilityQuestionWithoutSuppressionRemainsActionable()
    {
        var decision =
            SekoRequestRouter.Route(
                "Can you fix the Stop button in Seko and build the solution?");

        Assert.False(
            decision.UseFastConversation);

        Assert.False(
            decision.TaskIntent.ExecutionSuppressed);

        Assert.True(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.True(
            decision.TaskIntent.RequiresModification);
    }

    [Fact]
    public void Router_ReadOnlyInspectionRequestIsNotOverSuppressed()
    {
        var decision =
            SekoRequestRouter.Route(
                "Read your own code and tell me what could be improved. Do not change any files.");

        Assert.False(
            decision.UseFastConversation);

        Assert.False(
            decision.TaskIntent.ExecutionSuppressed);

        Assert.True(
            decision.TaskIntent.RequiresWorkspaceTools);

        Assert.False(
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
        var systemPrompt =
            GetNormalizedFastConversationSystemPrompt();

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
    public void FastConversationPromptRejectsInventedProceduralDetails()
    {
        var systemPrompt =
            GetNormalizedFastConversationSystemPrompt();

        Assert.Contains(
            "Do not fill a knowledge gap with a plausible-sounding detail",
            systemPrompt);

        Assert.Contains(
            "For how-to or procedural answers",
            systemPrompt);

        Assert.Contains(
            "do not invent special tools, materials, adhesives, timings, measurements, settings, preparation steps, waiting periods, or safety claims.",
            systemPrompt);
    }

    [Fact]
    public void FastConversationPromptForbidsFalseToolUseClaims()
    {
        var systemPrompt =
            GetNormalizedFastConversationSystemPrompt();

        Assert.Contains(
            "Do not say or imply that you searched or browsed the web",
            systemPrompt);

        Assert.Contains(
            "Never describe a fake research or execution plan.",
            systemPrompt);

        Assert.Contains(
            "instead of pretending verification happened.",
            systemPrompt);
    }

    [Fact]
    public void FastConversationPromptProtectsVersionSensitiveTechnicalWording()
    {
        var systemPrompt =
            GetNormalizedFastConversationSystemPrompt();

        Assert.Contains(
            "Avoid categorical wording such as \"only\", \"never\", or \"cannot\"",
            systemPrompt);

        Assert.Contains(
            "Modern C# interfaces can provide default member implementations.",
            systemPrompt);

        Assert.Contains(
            "do not guess from stale memory.",
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

        Assert.Equal(
            0.35,
            options["temperature"]!
                .GetValue<double>());
    }

    private static string GetNormalizedFastConversationSystemPrompt()
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

        var prompt =
            messages[0]!
                .AsObject()["content"]!
                .GetValue<string>();

        return
            Regex.Replace(
                prompt,
                @"\s+",
                " ")
            .Trim();
    }
}