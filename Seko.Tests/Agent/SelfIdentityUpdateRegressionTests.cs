using System.Text.Json;
using System.Text.Json.Nodes;
using Seko.Core.Chat;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent;
using Seko.Infrastructure.Diagnostics;

namespace Seko.Tests.Agent;

public sealed class SelfIdentityUpdateRegressionTests
{
    private const string ExactRequest =
        "Update yourself from v1.1.4 to v1.2.0 and rename yourself to S.E.K.O";

    [Fact]
    public void ExactRequest_IsParsedAsBoundedProductIdentityUpdate()
    {
        var intent =
            TaskIntentAnalyzer.Analyze(
                ExactRequest);

        Assert.True(
            intent.RequiresWorkspaceTools);

        Assert.True(
            intent.RequiresModification);

        Assert.True(
            intent.RequiresProductIdentityUpdate);

        Assert.Equal(
            "1.1.4",
            intent.ExpectedCurrentProductVersion);

        Assert.Equal(
            "1.2.0",
            intent.RequestedProductVersion);

        Assert.Equal(
            "S.E.K.O",
            intent.RequestedProductDisplayName);
    }

    [Fact]
    public void ProductIdentityPlanner_UsesNarrowInspectionAndThreeVerificationGates()
    {
        var inspection =
            SekoAutonomyToolPlanner.Create(
                new SekoAutonomyState
                {
                    Phase =
                        SekoAutonomyPhase.Inspection,

                    ProductIdentityUpdateRequired =
                        true,

                    WorkspaceModificationAllowed =
                        true
                });

        Assert.Equal(
            new[]
            {
                "inspect_product_identity"
            },
            inspection.ToolNames);

        var verification =
            SekoAutonomyToolPlanner.Create(
                new SekoAutonomyState
                {
                    Phase =
                        SekoAutonomyPhase.Verification,

                    ProductIdentityUpdateRequired =
                        true,

                    WorkspaceModificationAllowed =
                        true
                });

        Assert.Equal(
            new[]
            {
                "build_project",
                "test_project",
                "verify_product_identity"
            },
            verification.ToolNames);
    }

    [Fact]
    public async Task ExactIdentityUpdate_CompletesWithinBoundedInspectionAndCommitsAfterVerification()
    {
        var transport =
            new ScriptedTransport(
                ToolResponse(
                    "inspect_product_identity",
                    """
                    {
                      "expected_current_version": "1.1.4",
                      "requested_version": "1.2.0",
                      "requested_name": "S.E.K.O"
                    }
                    """),
                ToolResponse(
                    "replace_text",
                    """
                    {
                      "path": "Seko.Core/Product/SekoProductIdentity.cs",
                      "old_text": "public const string DisplayName =\n        \"SEKO\";\n\n    public const string Version =\n        \"1.1.4\";",
                      "new_text": "public const string DisplayName =\n        \"S.E.K.O\";\n\n    public const string Version =\n        \"1.2.0\";"
                    }
                    """),
                ToolResponse(
                    "build_project",
                    "{}"),
                ToolResponse(
                    "test_project",
                    "{}"),
                ToolResponse(
                    "verify_product_identity",
                    """
                    {
                      "expected_name": "S.E.K.O",
                      "expected_version": "1.2.0"
                    }
                    """),
                MessageResponse(
                    "Updated the product identity to S.E.K.O v1.2.0."));

        var toolHost =
            new ScriptedToolHost();

        toolHost.QueueResult(
            "inspect_product_identity",
            """
            PRODUCT IDENTITY INSPECTION PASSED
            CANONICAL PATH: Seko.Core/Product/SekoProductIdentity.cs
            CURRENT DISPLAY NAME: SEKO
            CURRENT VERSION: 1.1.4
            REQUESTED DISPLAY NAME: S.E.K.O
            REQUESTED VERSION: 1.2.0
            """);

        toolHost.QueueResult(
            "replace_text",
            "Updated Seko.Core/Product/SekoProductIdentity.cs.");

        toolHost.QueueResult(
            "build_project",
            "BUILD TARGET: Seko.sln\nBUILD EXIT CODE: 0");

        toolHost.QueueResult(
            "test_project",
            "TEST TARGET: Seko.sln\nTEST EXIT CODE: 0");

        toolHost.QueueResult(
            "verify_product_identity",
            "PRODUCT IDENTITY VERIFICATION PASSED: display_name=S.E.K.O; version=1.2.0; ui=canonical; conversation_identity=canonical; modification_generation=1.");

        var agent =
            new OllamaAgent(
                new Workspace
                {
                    Id =
                        Guid.NewGuid(),

                    Name =
                        "Seko",

                    RootPath =
                        Path.Combine(
                            Path.GetTempPath(),
                            "SekoIdentityRegression")
                },
                toolHost,
                transport,
                model:
                    "scripted-test-model");

        var diagnostics =
            new List<SekoDiagnosticEvent>();

        agent.DiagnosticEvent +=
            diagnostics.Add;

        var response =
            await agent.SendAsync(
                new[]
                {
                    new ChatMessage
                    {
                        Role =
                            MessageRole.User,

                        Content =
                            ExactRequest
                    }
                });

        Assert.Equal(
            "Updated the product identity to S.E.K.O v1.2.0.",
            response.Content);

        Assert.Equal(
            new[]
            {
                "inspect_product_identity",
                "replace_text",
                "build_project",
                "test_project",
                "verify_product_identity"
            },
            toolHost.ExecutedCalls
                .Select(
                    call => call.ToolName)
                .ToArray());

        Assert.Equal(
            1,
            toolHost.AutoCommitCallCount);

        Assert.Equal(
            6,
            transport.Requests.Count);

        Assert.Equal(
            new[]
            {
                "inspect_product_identity"
            },
            GetToolNames(
                transport.Requests[0]));

        Assert.Contains(
            "replace_text",
            GetToolNames(
                transport.Requests[1]));

        Assert.Equal(
            new[]
            {
                "build_project",
                "test_project",
                "verify_product_identity"
            },
            GetToolNames(
                transport.Requests[2]));

        Assert.Equal(
            new[]
            {
                "build_project",
                "test_project",
                "verify_product_identity"
            },
            GetToolNames(
                transport.Requests[3]));

        Assert.Equal(
            new[]
            {
                "build_project",
                "test_project",
                "verify_product_identity"
            },
            GetToolNames(
                transport.Requests[4]));

        Assert.Empty(
            GetToolNames(
                transport.Requests[5]));

        var inspectionRounds =
            diagnostics
                .Where(
                    diagnostic =>
                        diagnostic.Kind
                            == SekoDiagnosticEventKind.Autonomy
                        && diagnostic.Name.Equals(
                            "host.autonomy_round",
                            StringComparison.Ordinal)
                        && diagnostic.Arguments?.Contains(
                            "phase=Inspection",
                            StringComparison.Ordinal)
                            == true)
                .Count();

        Assert.True(
            inspectionRounds <= 3);

        Assert.DoesNotContain(
            "Phase budget exhausted for Inspection",
            response.Content,
            StringComparison.Ordinal);

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Kind
                    == SekoDiagnosticEventKind.Autonomy
                && diagnostic.Arguments?.Contains(
                    "phase=Complete",
                    StringComparison.Ordinal)
                    == true
                && diagnostic.Success
                    == true);
    }

    private static string ToolResponse(
        string toolName,
        string argumentsJson)
    {
        return
            new JsonObject
            {
                ["message"] =
                    new JsonObject
                    {
                        ["role"] =
                            "assistant",

                        ["content"] =
                            string.Empty,

                        ["tool_calls"] =
                            new JsonArray
                            {
                                new JsonObject
                                {
                                    ["function"] =
                                        new JsonObject
                                        {
                                            ["name"] =
                                                toolName,

                                            ["arguments"] =
                                                JsonNode.Parse(
                                                    argumentsJson)
                                        }
                                }
                            }
                    }
            }.ToJsonString();
    }

    private static string MessageResponse(
        string content)
    {
        return
            new JsonObject
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

    private static string[] GetToolNames(
        JsonObject request)
    {
        if (request["tools"]
            is not JsonArray tools)
        {
            return
                Array.Empty<string>();
        }

        return
            tools
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

    private sealed class ScriptedTransport :
        IOllamaChatTransport
    {
        private readonly Queue<string> _responses;

        public List<JsonObject> Requests { get; } =
            new();

        public ScriptedTransport(
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
                    "The scripted transport ran out of responses.");
            }

            return
                Task.FromResult(
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

        public int AutoCommitCallCount { get; private set; }

        public Task BeginTaskAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public JsonArray CreateToolDefinitions(
            IEnumerable<string> toolNames)
        {
            var result =
                new JsonArray();

            foreach (var toolName
                     in toolNames)
            {
                result.Add(
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
                                    "Scripted self-identity regression tool.",

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

            return result;
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
                    out var queue)
                || queue.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No scripted result exists for {toolName}.");
            }

            return
                Task.FromResult(
                    queue.Dequeue());
        }

        public string BuildAdaptiveContext(
            string currentTask)
        {
            return
                "Bounded self-identity regression context.";
        }

        public Task<string?> TryAutoCommitAsync(
            string userRequest,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AutoCommitCallCount++;

            return
                Task.FromResult<string?>(
                    null);
        }

        public void QueueResult(
            string toolName,
            string result)
        {
            if (!_results.TryGetValue(
                    toolName,
                    out var queue))
            {
                queue =
                    new Queue<string>();

                _results[toolName] =
                    queue;
            }

            queue.Enqueue(
                result);
        }
    }

    private sealed record ExecutedToolCall(
        string ToolName,
        string ArgumentsJson);
}