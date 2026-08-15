using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Seko.Core.Workspaces;
using Seko.Infrastructure.Agent;
using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Capabilities.BuiltIn;
using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Tools;
using Seko.Infrastructure.Agent.Web;
using Seko.Infrastructure.Diagnostics;

namespace Seko.Tests.Agent;

public sealed class AutonomyEfficiencyRegressionTests
{
    [Fact]
    public void MessageWindow_AlwaysKeepsNewestToolEvidence()
    {
        var method =
            typeof(OllamaAgent).GetMethod(
                "BuildBoundedWorkspaceMessages",
                BindingFlags.NonPublic
                | BindingFlags.Static);

        Assert.NotNull(
            method);

        var messages =
            new JsonArray
            {
                new JsonObject
                {
                    ["role"] =
                        "system",

                    ["content"] =
                        new string(
                            's',
                            21_500)
                },

                new JsonObject
                {
                    ["role"] =
                        "user",

                    ["content"] =
                        "current task"
                },

                new JsonObject
                {
                    ["role"] =
                        "assistant",

                    ["content"] =
                        string.Empty
                },

                new JsonObject
                {
                    ["role"] =
                        "tool",

                    ["tool_name"] =
                        "web_research",

                    ["content"] =
                        "LATEST_RESEARCH_EVIDENCE"
                }
            };

        var bounded =
            Assert.IsType<JsonArray>(
                method!.Invoke(
                    null,
                    new object[]
                    {
                        messages
                    }));

        Assert.Contains(
            "LATEST_RESEARCH_EVIDENCE",
            bounded.ToJsonString());
    }

    [Fact]
    public void ToolHost_FilteredDefinitionsExposeOnlyRequestedTools()
    {
        var root =
            CreateTemporaryDirectory();

        try
        {
            var host =
                new SekoToolHost(
                    new Workspace
                    {
                        Id =
                            Guid.NewGuid(),

                        Name =
                            "Tool filtering test",

                        RootPath =
                            root
                    });

            var definitions =
                host.CreateToolDefinitions(
                    new[]
                    {
                        "web_research",
                        "read_file"
                    });

            Assert.Equal(
                2,
                definitions.Count);

            var names =
                definitions
                    .Select(
                        node =>
                            node?["function"]?["name"]
                                ?.GetValue<string>())
                    .Where(
                        value =>
                            value is not null)
                    .Cast<string>()
                    .ToHashSet(
                        StringComparer.Ordinal);

            Assert.Contains(
                "web_research",
                names);

            Assert.Contains(
                "read_file",
                names);

            Assert.DoesNotContain(
                "write_file",
                names);

            Assert.DoesNotContain(
                "web_search",
                names);
        }
        finally
        {
            Directory.Delete(
                root,
                true);
        }
    }

    [Fact]
    public async Task ResearchCandidateSelectionPrefersDifferentHosts()
    {
        var fetchedHosts =
            new List<string>();

        var sync =
            new object();

        var service =
            ServiceWithAsyncResponse(
                request =>
                {
                    if (request.RequestUri!.Host.Equals(
                            "html.duckduckgo.com",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return
                            Task.FromResult(
                                TextResponse(
                                    SearchHtml(
                                        "https://a.example/one",
                                        "https://a.example/two",
                                        "https://b.example/one",
                                        "https://c.example/one"),
                                    "text/html"));
                    }

                    lock (sync)
                    {
                        fetchedHosts.Add(
                            request.RequestUri.Host);
                    }

                    return
                        Task.FromResult(
                            TextResponse(
                                $"<html><body>Source {request.RequestUri.Host}</body></html>",
                                "text/html"));
                });

        var packet =
            await service.ResearchAsync(
                "host diversity",
                3,
                2_000);

        Assert.Equal(
            3,
            packet.Sources.Count);

        Assert.Equal(
            3,
            fetchedHosts
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.Contains(
            "a.example",
            fetchedHosts);

        Assert.Contains(
            "b.example",
            fetchedHosts);

        Assert.Contains(
            "c.example",
            fetchedHosts);
    }

    [Fact]
    public async Task ResearchAggregateFetchesSelectedSourcesInParallel()
    {
        var activeFetches =
            0;

        var maximumConcurrentFetches =
            0;

        var sync =
            new object();

        var service =
            ServiceWithAsyncResponse(
                async request =>
                {
                    if (request.RequestUri!.Host.Equals(
                            "html.duckduckgo.com",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return
                            TextResponse(
                                SearchHtml(
                                    "https://a.example/article",
                                    "https://b.example/article",
                                    "https://c.example/article"),
                                "text/html");
                    }

                    lock (sync)
                    {
                        activeFetches++;

                        maximumConcurrentFetches =
                            Math.Max(
                                maximumConcurrentFetches,
                                activeFetches);
                    }

                    try
                    {
                        await Task.Delay(
                            80);

                        return
                            TextResponse(
                                $"<html><body>Source {request.RequestUri.Host}</body></html>",
                                "text/html");
                    }
                    finally
                    {
                        lock (sync)
                        {
                            activeFetches--;
                        }
                    }
                });

        var packet =
            await service.ResearchAsync(
                "parallel research",
                3,
                2_000);

        Assert.Equal(
            3,
            packet.Sources.Count);

        Assert.True(
            maximumConcurrentFetches >= 2);
    }

    [Fact]
    public async Task ResearchAggregateKeepsGoodSourcesWhenOneFetchFails()
    {
        var service =
            ServiceWithAsyncResponse(
                request =>
                {
                    if (request.RequestUri!.Host.Equals(
                            "html.duckduckgo.com",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return
                            Task.FromResult(
                                TextResponse(
                                    SearchHtml(
                                        "https://a.example/article",
                                        "https://b.example/article"),
                                    "text/html"));
                    }

                    if (request.RequestUri.Host.Equals(
                            "b.example",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return
                            Task.FromResult(
                                new HttpResponseMessage(
                                    HttpStatusCode.InternalServerError));
                    }

                    return
                        Task.FromResult(
                            TextResponse(
                                "<html><body>Good source</body></html>",
                                "text/html"));
                });

        var packet =
            await service.ResearchAsync(
                "partial failure",
                2,
                2_000);

        Assert.Equal(
            2,
            packet.Sources.Count);

        Assert.Single(
            packet.Sources.Where(
                source =>
                    source.Page is not null));

        Assert.Single(
            packet.Sources.Where(
                source =>
                    !string.IsNullOrWhiteSpace(
                        source.Error)));
    }

    [Fact]
    public async Task ResearchAggregateBoundsEachSource()
    {
        var service =
            ServiceWithAsyncResponse(
                request =>
                {
                    if (request.RequestUri!.Host.Equals(
                            "html.duckduckgo.com",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return
                            Task.FromResult(
                                TextResponse(
                                    SearchHtml(
                                        "https://a.example/article"),
                                    "text/html"));
                    }

                    return
                        Task.FromResult(
                            TextResponse(
                                "<html><body>"
                                + new string(
                                    'x',
                                    8_000)
                                + "</body></html>",
                                "text/html"));
                });

        var packet =
            await service.ResearchAsync(
                "bounded source",
                1,
                2_000);

        var source =
            Assert.Single(
                packet.Sources);

        Assert.NotNull(
            source.Page);

        Assert.True(
            source.Page!.Text.Length <= 2_000);

        Assert.True(
            source.Page.Truncated);
    }

    [Fact]
    public async Task WebCapability_RegistersAndExecutesAggregateResearchTool()
    {
        var service =
            ServiceWithAsyncResponse(
                request =>
                {
                    if (request.RequestUri!.Host.Equals(
                            "html.duckduckgo.com",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return
                            Task.FromResult(
                                TextResponse(
                                    SearchHtml(
                                        "https://a.example/article"),
                                    "text/html"));
                    }

                    return
                        Task.FromResult(
                            TextResponse(
                                "<html><head><title>Official source</title></head><body>Verified facts.</body></html>",
                                "text/html"));
                });

        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        capabilityRegistry.Register(
            new WebResearchCapability(
                service),
            CapabilitySource.BuiltIn,
            SekoPermissionPolicy.CreateDefault(),
            toolRegistry);

        var output =
            await toolRegistry.ExecuteAsync(
                "web_research",
                """
                {
                  "query": "example",
                  "max_sources": 1,
                  "max_chars_per_source": 2000
                }
                """);

        Assert.Contains(
            "UNTRUSTED WEB RESEARCH PACKET",
            output);

        Assert.Contains(
            "Fetch status: SUCCESS",
            output);

        Assert.Contains(
            "Verified facts.",
            output);
    }

    [Fact]
    public void WebCapability_AdvertisesAggregateResearchAbility()
    {
        var capability =
            new WebResearchCapability(
                ServiceWithAsyncResponse(
                    _ =>
                        Task.FromResult(
                            TextResponse(
                                string.Empty,
                                "text/html"))));

        Assert.Contains(
            "research.aggregate",
            capability.Descriptor.Abilities);

        Assert.Contains(
            capability.Tools,
            tool =>
                tool.Name.Equals(
                    "web_research",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void TaskLogger_WritesExactToolExecutionSummaryNearTop()
    {
        var root =
            CreateTemporaryDirectory();

        try
        {
            var logger =
                new SekoTaskLogger(
                    root);

            var session =
                logger.TryStart(
                    new Workspace
                    {
                        Id =
                            Guid.NewGuid(),

                        Name =
                            "Diagnostics test",

                        RootPath =
                            root
                    },
                    "test-model",
                    "diagnose the task");

            Assert.NotNull(
                session);

            logger.TryRecordDiagnostic(
                session,
                new SekoDiagnosticEvent(
                    DateTimeOffset.Now,
                    SekoDiagnosticEventKind.Tool,
                    "web_research",
                    TimeSpan.FromMilliseconds(
                        120),
                    """{"query":"latest .NET"}""",
                    "packet",
                    true));

            logger.TryRecordDiagnostic(
                session,
                new SekoDiagnosticEvent(
                    DateTimeOffset.Now,
                    SekoDiagnosticEventKind.Tool,
                    "web_research",
                    TimeSpan.Zero,
                    """{"query":"latest .NET"}""",
                    "Repeated semantic tool call blocked. Previous result was reused instead of executing the same call again.",
                    null));

            logger.TryRecordDiagnostic(
                session,
                new SekoDiagnosticEvent(
                    DateTimeOffset.Now,
                    SekoDiagnosticEventKind.Tool,
                    "search_workspace",
                    TimeSpan.FromMilliseconds(
                        30),
                    """{"query":"TargetFramework"}""",
                    "ERROR: simulated failure",
                    false));

            logger.TryRecordDiagnostic(
                session,
                new SekoDiagnosticEvent(
                    DateTimeOffset.Now,
                    SekoDiagnosticEventKind.Tool,
                    "host.phase_tool_blocked",
                    TimeSpan.Zero,
                    "phase=WorkspaceInspection; tool=web_search",
                    "The model requested an out-of-phase tool.",
                    null));

            logger.TryRecordDiagnostic(
                session,
                new SekoDiagnosticEvent(
                    DateTimeOffset.Now,
                    SekoDiagnosticEventKind.Autonomy,
                    "host.autonomy_stall",
                    TimeSpan.Zero,
                    "phase=Incomplete; disposition=Incomplete; total_rounds=32; phase_rounds=6; no_progress=2; repairs=0; modification_generation=0; verified_generation=-1; research_completed=False; workspace_evidence=False; write_allowed=False",
                    "No meaningful progress for 2 consecutive rounds in Inspection.",
                    false));

            logger.TryFinish(
                session,
                "Incomplete",
                "test");

            var log =
                File.ReadAllText(
                    session!.FilePath);

            Assert.Contains(
                "## Tool execution summary",
                log);

            var toolSummaryIndex =
                log.IndexOf(
                    "## Tool execution summary",
                    StringComparison.Ordinal);

            var autonomySummaryIndex =
                log.IndexOf(
                    "## Autonomy summary",
                    StringComparison.Ordinal);

            var diagnosticsIndex =
                log.IndexOf(
                    "## Diagnostic events",
                    StringComparison.Ordinal);

            Assert.True(
                toolSummaryIndex
                < autonomySummaryIndex);

            Assert.True(
                autonomySummaryIndex
                < diagnosticsIndex);

            Assert.Contains(
                "- Model tool requests: **3**",
                log);

            Assert.Contains(
                "- Executed tool calls: **2**",
                log);

            Assert.Contains(
                "- Successful executions: **1**",
                log);

            Assert.Contains(
                "- Failed/cancelled executions: **1**",
                log);

            Assert.Contains(
                "- Blocked semantic duplicates: **1**",
                log);

            Assert.Contains(
                "- Blocked out-of-phase requests: **1**",
                log);

            Assert.Contains(
                "## Autonomy summary",
                log);

            Assert.Contains(
                "- Controller decisions: **1**",
                log);

            Assert.Contains(
                "- Final controller event: `host.autonomy_stall`",
                log);

            Assert.Contains(
                "- Final controller outcome: **Incomplete**",
                log);

            Assert.Contains(
                "phase=Incomplete",
                log);

            Assert.Contains(
                "No meaningful progress for 2 consecutive rounds in Inspection.",
                log);

            Assert.Contains(
                "web_research=2",
                log);

            Assert.Contains(
                "search_workspace=1",
                log);

            Assert.Contains(
                "| 1 |",
                log);

            Assert.Contains(
                "| 2 |",
                log);

            Assert.Contains(
                "| 3 |",
                log);
        }
        finally
        {
            Directory.Delete(
                root,
                true);
        }
    }

    private static WebAddressGuard PublicGuard()
    {
        return
            new WebAddressGuard(
                (_, _) =>
                    Task.FromResult(
                        new[]
                        {
                            IPAddress.Parse(
                                "93.184.216.34")
                        }));
    }

    private static WebResearchService ServiceWithAsyncResponse(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var client =
            new HttpClient(
                new AsyncStubHttpMessageHandler(
                    responder));

        return
            new WebResearchService(
                client,
                PublicGuard());
    }

    private static string SearchHtml(
        params string[] urls)
    {
        var builder =
            new StringBuilder(
                "<html><body>");

        for (var index = 0;
             index < urls.Length;
             index++)
        {
            builder.Append(
                $"""
                <a class="result__a" href="{urls[index]}">Source {index + 1}</a>
                <a class="result__snippet">Snippet {index + 1}</a>
                """);
        }

        builder.Append(
            "</body></html>");

        return
            builder.ToString();
    }

    private static HttpResponseMessage TextResponse(
        string text,
        string contentType)
    {
        var response =
            new HttpResponseMessage(
                HttpStatusCode.OK);

        response.Content =
            new StringContent(
                text,
                Encoding.UTF8,
                contentType);

        return response;
    }

    private static string CreateTemporaryDirectory()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                "SekoTests",
                Guid.NewGuid()
                    .ToString(
                        "N"));

        Directory.CreateDirectory(
            path);

        return path;
    }

    private sealed class AsyncStubHttpMessageHandler :
        HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public AsyncStubHttpMessageHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder =
                responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return
                _responder(
                    request);
        }
    }
}
