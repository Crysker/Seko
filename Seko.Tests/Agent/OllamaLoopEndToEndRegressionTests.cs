using System.Text.Json;
using System.Text.Json.Nodes;
using Seko.Core.Chat;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class OllamaLoopEndToEndRegressionTests
{
    private static readonly string[] InspectionTools =
    {
        "search_workspace",
        "find_files",
        "find_text",
        "list_files",
        "read_file",
        "read_task_log"
    };

    private static readonly string[] ActionTools =
    {
        "search_workspace",
        "find_files",
        "find_text",
        "list_files",
        "read_file",
        "write_file",
        "replace_text",
        "build_project",
        "git_status",
        "git_diff"
    };

    private static readonly string[] VerificationTools =
    {
        "search_workspace",
        "find_files",
        "find_text",
        "list_files",
        "read_file",
        "build_project",
        "git_status",
        "git_diff"
    };

    [Fact]
    public async Task ModificationFlow_ExercisesRealControllerPlannerAndAgentLoop()
    {
        var transport =
            new ScriptedOllamaChatTransport(
                ToolResponse(
                    ("read_file", "{\"path\":\"App.cs\"}")),
                MessageResponse(
                    "Inspection is complete."),
                ToolResponse(
                    ("write_file", "{\"path\":\"App.cs\",\"content\":\"updated\"}")),
                ToolResponse(
                    ("build_project", "{}")),
                MessageResponse(
                    "Implemented and verified."));

        var toolHost =
            new ScriptedToolHost();

        toolHost.QueueResult(
            "read_file",
            "class App { }");

        toolHost.QueueResult(
            "write_file",
            "Wrote App.cs");

        toolHost.QueueResult(
            "build_project",
            "BUILD EXIT CODE: 0");

        var agent =
            CreateAgent(
                toolHost,
                transport);

        var response =
            await agent.SendAsync(
                UserConversation(
                    "Fix the Stop button in Seko and build the project."));

        Assert.Equal(
            "Implemented and verified.",
            response.Content);

        Assert.Equal(
            new[]
            {
                "read_file",
                "write_file",
                "build_project"
            },
            toolHost.ExecutedCalls
                .Select(
                    call => call.ToolName)
                .ToArray());

        Assert.Equal(
            1,
            toolHost.BeginTaskCallCount);

        Assert.Equal(
            1,
            toolHost.AutoCommitCallCount);

        Assert.Equal(
            5,
            transport.Requests.Count);

        AssertToolNames(
            transport.Requests[0],
            InspectionTools);

        AssertToolNames(
            transport.Requests[1],
            InspectionTools);

        AssertToolNames(
            transport.Requests[2],
            ActionTools);

        AssertToolNames(
            transport.Requests[3],
            VerificationTools);

        AssertToolNames(
            transport.Requests[4],
            Array.Empty<string>());
    }

    [Fact]
    public async Task SameResponse_WriteAfterActionTransition_IsBlockedBeforeExecution()
    {
        var transport =
            new ScriptedOllamaChatTransport(
                ToolResponse(
                    ("read_file", "{\"path\":\"App.cs\"}")),
                MessageResponse(
                    "Inspection is complete."),
                ToolResponse(
                    ("write_file", "{\"path\":\"App.cs\",\"content\":\"updated\"}"),
                    ("replace_text", "{\"path\":\"App.cs\",\"old_text\":\"updated\",\"new_text\":\"changed again\"}")),
                ToolResponse(
                    ("build_project", "{}")),
                MessageResponse(
                    "Done."));

        var toolHost =
            new ScriptedToolHost();

        toolHost.QueueResult(
            "read_file",
            "class App { }");

        toolHost.QueueResult(
            "write_file",
            "Wrote App.cs");

        toolHost.QueueResult(
            "build_project",
            "BUILD EXIT CODE: 0");

        var agent =
            CreateAgent(
                toolHost,
                transport);

        var response =
            await agent.SendAsync(
                UserConversation(
                    "Fix the Stop button in Seko and build the project."));

        Assert.Equal(
            "Done.",
            response.Content);

        Assert.Equal(
            new[]
            {
                "read_file",
                "write_file",
                "build_project"
            },
            toolHost.ExecutedCalls
                .Select(
                    call => call.ToolName)
                .ToArray());

        Assert.DoesNotContain(
            toolHost.ExecutedCalls,
            call =>
                call.ToolName.Equals(
                    "replace_text",
                    StringComparison.Ordinal));

        AssertToolNames(
            transport.Requests[3],
            VerificationTools);
    }

    [Fact]
    public async Task BuildOnlyFailure_StartsInVerificationAndCannotGainWritePermission()
    {
        var transport =
            new ScriptedOllamaChatTransport(
                ToolResponse(
                    ("build_project", "{}"),
                    ("write_file", "{\"path\":\"App.cs\",\"content\":\"unauthorized\"}")));

        var toolHost =
            new ScriptedToolHost();

        toolHost.QueueResult(
            "build_project",
            "BUILD EXIT CODE: 1\nCS1002: ; expected");

        var agent =
            CreateAgent(
                toolHost,
                transport);

        var response =
            await agent.SendAsync(
                UserConversation(
                    "Build the project."));

        Assert.Single(
            transport.Requests);

        AssertToolNames(
            transport.Requests[0],
            VerificationTools);

        Assert.Equal(
            new[]
            {
                "build_project"
            },
            toolHost.ExecutedCalls
                .Select(
                    call => call.ToolName)
                .ToArray());

        Assert.DoesNotContain(
            toolHost.ExecutedCalls,
            call =>
                call.ToolName.Equals(
                    "write_file",
                    StringComparison.Ordinal));

        Assert.Equal(
            0,
            toolHost.AutoCommitCallCount);

        Assert.Contains(
            "did not grant workspace modification permission",
            response.Content);
    }

    [Fact]
    public async Task StalledInspection_StopsFromControllerNoProgressPolicy()
    {
        var transport =
            new ScriptedOllamaChatTransport(
                MessageResponse(
                    "I should probably inspect something."),
                MessageResponse(
                    "I still have not inspected anything."));

        var toolHost =
            new ScriptedToolHost();

        var agent =
            CreateAgent(
                toolHost,
                transport);

        var response =
            await agent.SendAsync(
                UserConversation(
                    "Inspect the project without changing anything."));

        Assert.Equal(
            2,
            transport.Requests.Count);

        AssertToolNames(
            transport.Requests[0],
            InspectionTools);

        AssertToolNames(
            transport.Requests[1],
            InspectionTools);

        Assert.Empty(
            toolHost.ExecutedCalls);

        Assert.Equal(
            0,
            toolHost.AutoCommitCallCount);

        Assert.Contains(
            "No meaningful progress for 2 consecutive rounds in Inspection",
            response.Content);
    }

    [Fact]
    public async Task FastConversation_UsesInjectedTransportWithoutStartingToolTask()
    {
        var transport =
            new ScriptedOllamaChatTransport(
                MessageResponse(
                    "A deterministic test joke."));

        var toolHost =
            new ScriptedToolHost();

        var agent =
            CreateAgent(
                toolHost,
                transport);

        var response =
            await agent.SendAsync(
                UserConversation(
                    "Tell me a joke."));

        Assert.Equal(
            "A deterministic test joke.",
            response.Content);

        Assert.Equal(
            0,
            toolHost.BeginTaskCallCount);

        Assert.Equal(
            0,
            toolHost.AutoCommitCallCount);

        Assert.Empty(
            toolHost.ExecutedCalls);

        Assert.Single(
            transport.Requests);

        AssertToolNames(
            transport.Requests[0],
            Array.Empty<string>());
    }

    private static OllamaAgent CreateAgent(
        ScriptedToolHost toolHost,
        ScriptedOllamaChatTransport transport)
    {
        return new OllamaAgent(
            new Workspace
            {
                Id =
                    Guid.NewGuid(),

                Name =
                    "ScriptedLoopWorkspace",

                RootPath =
                    Path.Combine(
                        Path.GetTempPath(),
                        "SekoScriptedLoopTests")
            },
            toolHost,
            transport,
            model:
                "scripted-test-model");
    }

    private static IReadOnlyList<ChatMessage> UserConversation(
        string request)
    {
        return new[]
        {
            new ChatMessage
            {
                Role =
                    MessageRole.User,

                Content =
                    request
            }
        };
    }

    private static void AssertToolNames(
        JsonObject request,
        IReadOnlyCollection<string> expected)
    {
        Assert.Equal(
            expected.ToArray(),
            GetToolNames(
                request));
    }

    private static string[] GetToolNames(
        JsonObject request)
    {
        if (request["tools"]
            is not JsonArray tools)
        {
            return
                Array.Empty<string>();
        }

        return tools
            .Select(
                definition =>
                    definition?["function"]?["name"]
                        ?.GetValue<string>()
                    ?? string.Empty)
            .Where(
                name =>
                    !string.IsNullOrWhiteSpace(
                        name))
            .ToArray();
    }

    private static string MessageResponse(
        string content)
    {
        return new JsonObject
        {
            ["message"] =
                new JsonObject
                {
                    ["role"] =
                        "assistant",

                    ["content"] =
                        content
                }
        }.ToJsonString();
    }

    private static string ToolResponse(
        params (string Name, string ArgumentsJson)[] calls)
    {
        var toolCalls =
            new JsonArray();

        foreach (var call
                 in calls)
        {
            toolCalls.Add(
                new JsonObject
                {
                    ["function"] =
                        new JsonObject
                        {
                            ["name"] =
                                call.Name,

                            ["arguments"] =
                                JsonNode.Parse(
                                    call.ArgumentsJson)
                        }
                });
        }

        return new JsonObject
        {
            ["message"] =
                new JsonObject
                {
                    ["role"] =
                        "assistant",

                    ["content"] =
                        string.Empty,

                    ["tool_calls"] =
                        toolCalls
                }
        }.ToJsonString();
    }

    private sealed class ScriptedOllamaChatTransport :
        IOllamaChatTransport
    {
        private readonly Queue<string> _responses;

        public List<JsonObject> Requests { get; } =
            new();

        public ScriptedOllamaChatTransport(
            params string[] responses)
        {
            _responses =
                new Queue<string>(
                    responses);
        }

        public Task<JsonDocument> SendAsync(
            JsonObject request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(
                (JsonObject)request.DeepClone());

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "The scripted Ollama transport ran out of responses.");
            }

            return Task.FromResult(
                JsonDocument.Parse(
                    _responses.Dequeue()));
        }
    }

    private sealed class ScriptedToolHost :
        ISekoToolHost
    {
        private readonly Dictionary<string, Queue<string>> _results =
            new(
                StringComparer.Ordinal);

        public List<ExecutedToolCall> ExecutedCalls { get; } =
            new();

        public int BeginTaskCallCount { get; private set; }

        public int AutoCommitCallCount { get; private set; }

        public Task BeginTaskAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BeginTaskCallCount++;

            return Task.CompletedTask;
        }

        public JsonArray CreateToolDefinitions(
            IEnumerable<string> toolNames)
        {
            ArgumentNullException.ThrowIfNull(
                toolNames);

            var definitions =
                new JsonArray();

            foreach (var toolName
                     in toolNames)
            {
                definitions.Add(
                    new JsonObject
                    {
                        ["type"] =
                            "function",

                        ["function"] =
                            new JsonObject
                            {
                                ["name"] =
                                    toolName,

                                ["description"] =
                                    "Scripted regression-test tool.",

                                ["parameters"] =
                                    new JsonObject
                                    {
                                        ["type"] =
                                            "object",

                                        ["properties"] =
                                            new JsonObject()
                                    }
                            }
                    });
            }

            return definitions;
        }

        public Task<string> ExecuteAsync(
            string toolName,
            string argumentsJson,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ExecutedCalls.Add(
                new ExecutedToolCall(
                    toolName,
                    argumentsJson));

            if (!_results.TryGetValue(
                    toolName,
                    out var results)
                || results.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No scripted result exists for tool '{toolName}'.");
            }

            return Task.FromResult(
                results.Dequeue());
        }

        public string BuildAdaptiveContext(
            string currentTask)
        {
            return
                "Deterministic scripted tool host.";
        }

        public Task<string?> TryAutoCommitAsync(
            string userRequest,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AutoCommitCallCount++;

            return Task.FromResult<string?>(
                null);
        }

        public void QueueResult(
            string toolName,
            string result)
        {
            if (!_results.TryGetValue(
                    toolName,
                    out var results))
            {
                results =
                    new Queue<string>();

                _results[toolName] =
                    results;
            }

            results.Enqueue(
                result);
        }
    }

    private sealed record ExecutedToolCall(
        string ToolName,
        string ArgumentsJson);
}