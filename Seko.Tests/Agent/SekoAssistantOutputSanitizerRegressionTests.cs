using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class SekoAssistantOutputSanitizerRegressionTests
{
    [Fact]
    public void CompleteThinkBlock_IsRemoved()
    {
        var sanitized =
            SekoAssistantOutputSanitizer.Sanitize(
                "<think>private reasoning</think>Final answer.");

        Assert.Equal(
            "Final answer.",
            sanitized);
    }

    [Fact]
    public void StrayClosingThinkMarker_DropsLeakedPrefix()
    {
        var sanitized =
            SekoAssistantOutputSanitizer.Sanitize(
                "private reasoning that must not escape</think>Final answer.");

        Assert.Equal(
            "Final answer.",
            sanitized);
    }

    [Fact]
    public void NestedThinkBlocks_AreRemovedAsOneReasoningRegion()
    {
        var sanitized =
            SekoAssistantOutputSanitizer.Sanitize(
                "<think>outer <think>inner</think> still outer</think>Visible.");

        Assert.Equal(
            "Visible.",
            sanitized);
    }

    [Theory]
    [InlineData(
        "<think>unfinished private reasoning",
        "")]
    [InlineData(
        "Visible first.<think>unfinished private reasoning",
        "Visible first.")]
    [InlineData(
        "<think>outer<think>inner</think>still hidden",
        "")]
    public void MalformedThinkBlocks_FailClosed(
        string input,
        string expected)
    {
        var sanitized =
            SekoAssistantOutputSanitizer.Sanitize(
                input);

        Assert.Equal(
            expected,
            sanitized);
    }

    [Theory]
    [InlineData(
        "```csharp\nvar values = new List<string>();\nif (left < right) { Console.WriteLine(\"ok\"); }\n```")]
    [InlineData(
        "<root><item id=\"1\">value</item><thinking>ordinary XML element</thinking></root>")]
    [InlineData(
        "<main><section><strong>Hello</strong><br /></section></main>")]
    [InlineData(
        "<think-data>ordinary custom element</think-data>")]
    public void NormalCodeXmlAndHtml_ArePreservedExactly(
        string input)
    {
        var sanitized =
            SekoAssistantOutputSanitizer.Sanitize(
                input);

        Assert.Equal(
            input,
            sanitized);
    }

    [Fact]
    public void AssistantMessageMetadata_IsPreservedWhenContentIsSanitized()
    {
        var id =
            Guid.NewGuid();

        var createdAt =
            DateTimeOffset.Now.AddMinutes(
                -2);

        var message =
            new ChatMessage
            {
                Id =
                    id,

                Role =
                    MessageRole.Assistant,

                Content =
                    "<think>hidden</think>Visible.",

                CreatedAt =
                    createdAt
            };

        var sanitized =
            SekoAssistantOutputSanitizer.Sanitize(
                message);

        Assert.Equal(
            id,
            sanitized.Id);

        Assert.Equal(
            createdAt,
            sanitized.CreatedAt);

        Assert.Equal(
            MessageRole.Assistant,
            sanitized.Role);

        Assert.Equal(
            "Visible.",
            sanitized.Content);
    }

    [Fact]
    public void NonAssistantMessages_AreNotModified()
    {
        var message =
            new ChatMessage
            {
                Role =
                    MessageRole.User,

                Content =
                    "Show me the literal tag <think> in an example."
            };

        var sanitized =
            SekoAssistantOutputSanitizer.Sanitize(
                message);

        Assert.Same(
            message,
            sanitized);
    }

    [Fact]
    public async Task TransactionalBoundary_SanitizesBeforeReturningResponse()
    {
        var workspaceDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "SekoReasoningBoundaryRegressionTests",
                Guid.NewGuid()
                    .ToString("N"));

        Directory.CreateDirectory(
            workspaceDirectory);

        try
        {
            var innerAgent =
                new LeakyAssistantAgent(
                    "hidden scratchpad</think>Safe final answer.");

            var agent =
                new SekoTransactionalAgent(
                    new Workspace
                    {
                        Id =
                            Guid.NewGuid(),

                        Name =
                            "Reasoning boundary test",

                        RootPath =
                            workspaceDirectory
                    },
                    innerAgent);

            var response =
                await agent.SendAsync(
                    new[]
                    {
                        new ChatMessage
                        {
                            Role =
                                MessageRole.User,

                            Content =
                                "Hello"
                        }
                    });

            Assert.Equal(
                "Safe final answer.",
                response.Content);

            Assert.DoesNotContain(
                "hidden scratchpad",
                response.Content,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "</think>",
                response.Content,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(
                workspaceDirectory,
                true);
        }
    }

    private sealed class LeakyAssistantAgent :
        IAgent,
        IAgentActivitySource
    {
        private readonly string _content;

        public LeakyAssistantAgent(
            string content)
        {
            _content =
                content;
        }

        public event Action<AgentActivity>? ActivityChanged;

        public Task<ChatMessage> SendAsync(
            IReadOnlyList<ChatMessage> conversation,
            CancellationToken cancellationToken = default)
        {
            ActivityChanged?.Invoke(
                new AgentActivity(
                    AgentActivityKind.Completed,
                    "Done."));

            return
                Task.FromResult(
                    new ChatMessage
                    {
                        Role =
                            MessageRole.Assistant,

                        Content =
                            _content
                    });
        }
    }
}