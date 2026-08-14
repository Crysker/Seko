using System.Net;
using System.Text;
using Seko.Infrastructure.Agent;
using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Capabilities.BuiltIn;
using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Tools;
using Seko.Infrastructure.Agent.Web;

namespace Seko.Tests.Agent;

public sealed class WebResearchRegressionTests
{
    [Fact]
    public async Task AddressGuard_RejectsLocalhost()
    {
        var guard =
            new WebAddressGuard(
                (_, _) =>
                    Task.FromResult(
                        new[]
                        {
                            IPAddress.Parse(
                                "93.184.216.34")
                        }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                guard.ValidateAsync(
                    "http://localhost/test"));
    }

    [Fact]
    public async Task AddressGuard_RejectsPrivateLiteralAddress()
    {
        var guard =
            new WebAddressGuard();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                guard.ValidateAsync(
                    "http://192.168.1.10/secret"));
    }

    [Fact]
    public async Task AddressGuard_RejectsPrivateDnsAnswer()
    {
        var guard =
            new WebAddressGuard(
                (_, _) =>
                    Task.FromResult(
                        new[]
                        {
                            IPAddress.Parse(
                                "10.0.0.5")
                        }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                guard.ValidateAsync(
                    "https://example.test/"));
    }

    [Fact]
    public async Task AddressGuard_RejectsMixedPublicAndPrivateDnsAnswers()
    {
        var guard =
            new WebAddressGuard(
                (_, _) =>
                    Task.FromResult(
                        new[]
                        {
                            IPAddress.Parse(
                                "93.184.216.34"),

                            IPAddress.Parse(
                                "127.0.0.1")
                        }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                guard.ValidateAsync(
                    "https://example.test/"));
    }

    [Fact]
    public async Task AddressGuard_AllowsPublicHttpsHost()
    {
        var guard =
            PublicGuard();

        var uri =
            await guard.ValidateAsync(
                "https://example.test/article");

        Assert.Equal(
            "example.test",
            uri.Host);
    }

    [Fact]
    public async Task AddressGuard_RejectsEmbeddedCredentials()
    {
        var guard =
            PublicGuard();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                guard.ValidateAsync(
                    "https://user:secret@example.test/"));
    }

    [Fact]
    public async Task AddressGuard_RejectsNonStandardPort()
    {
        var guard =
            PublicGuard();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                guard.ValidateAsync(
                    "https://example.test:8443/"));
    }

    [Fact]
    public void TextExtractor_RemovesScriptsAndDecodesHtml()
    {
        const string html =
            """
            <html>
              <head><title>Example &amp; Test</title></head>
              <body>
                <script>ignore me</script>
                <h1>Hello &amp; welcome</h1>
                <p>Second line.</p>
              </body>
            </html>
            """;

        Assert.Equal(
            "Example & Test",
            WebTextExtractor.ExtractTitle(
                html));

        var text =
            WebTextExtractor.ExtractReadableText(
                html);

        Assert.Contains(
            "Hello & welcome",
            text);

        Assert.Contains(
            "Second line.",
            text);

        Assert.DoesNotContain(
            "ignore me",
            text);
    }

    [Fact]
    public async Task Search_ParsesDuckDuckGoResultAndUnwrapsUrl()
    {
        const string html =
            """
            <html>
              <body>
                <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Farticle&amp;rut=x">
                  Example &amp; Article
                </a>
                <a class="result__snippet">Useful <b>snippet</b> text.</a>
              </body>
            </html>
            """;

        var service =
            ServiceWithResponse(
                _ =>
                    TextResponse(
                        html,
                        "text/html"));

        var results =
            await service.SearchAsync(
                "example",
                5);

        var result =
            Assert.Single(
                results);

        Assert.Equal(
            "Example & Article",
            result.Title);

        Assert.Equal(
            "https://example.com/article",
            result.Url.TrimEnd('/'));

        Assert.Equal(
            "Useful snippet text.",
            result.Snippet);
    }

    [Fact]
    public async Task Fetch_FollowsRedirectAndExtractsReadableHtml()
    {
        var service =
            ServiceWithResponse(
                request =>
                {
                    if (request.RequestUri!.AbsolutePath
                        == "/start")
                    {
                        var response =
                            new HttpResponseMessage(
                                HttpStatusCode.Redirect);

                        response.Headers.Location =
                            new Uri(
                                "https://example.test/final");

                        return response;
                    }

                    return
                        TextResponse(
                            """
                            <html>
                              <head><title>Final Page</title></head>
                              <body><h1>Verified source</h1><p>Details.</p></body>
                            </html>
                            """,
                            "text/html");
                });

        var result =
            await service.FetchAsync(
                "https://example.test/start",
                4_000);

        Assert.Equal(
            "https://example.test/final",
            result.FinalUrl.TrimEnd('/'));

        Assert.Equal(
            "Final Page",
            result.Title);

        Assert.Contains(
            "Verified source",
            result.Text);
    }

    [Fact]
    public async Task Fetch_RejectsBinaryContentType()
    {
        var service =
            ServiceWithResponse(
                _ =>
                {
                    var response =
                        new HttpResponseMessage(
                            HttpStatusCode.OK);

                    response.Content =
                        new ByteArrayContent(
                            new byte[]
                            {
                                1,
                                2,
                                3
                            });

                    response.Content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue(
                            "application/octet-stream");

                    return response;
                });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.FetchAsync(
                    "https://example.test/file.bin"));
    }

    [Fact]
    public async Task Fetch_BoundsReadableOutput()
    {
        var service =
            ServiceWithResponse(
                _ =>
                    TextResponse(
                        "<html><body><p>"
                        + new string(
                            'x',
                            20_000)
                        + "</p></body></html>",
                        "text/html"));

        var result =
            await service.FetchAsync(
                "https://example.test/large",
                2_000);

        Assert.True(
            result.Text.Length <= 2_000);

        Assert.True(
            result.Truncated);
    }

    [Fact]
    public void WebCapability_AdvertisesGenericResearchAbilities()
    {
        var capability =
            new WebResearchCapability(
                ServiceWithResponse(
                    _ =>
                        TextResponse(
                            string.Empty,
                            "text/html")));

        Assert.Contains(
            "web.search",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "web.fetch",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "research.web",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "network.public-web",
            capability.Descriptor.RequiredPermissions);
    }

    [Fact]
    public async Task WebCapability_RegistersAndExecutesSearchTool()
    {
        const string html =
            """
            <a class="result__a" href="https://example.com/">Example</a>
            <a class="result__snippet">A result.</a>
            """;

        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        capabilityRegistry.Register(
            new WebResearchCapability(
                ServiceWithResponse(
                    _ =>
                        TextResponse(
                            html,
                            "text/html"))),
            CapabilitySource.BuiltIn,
            SekoPermissionPolicy.CreateDefault(),
            toolRegistry);

        var output =
            await toolRegistry.ExecuteAsync(
                "web_search",
                """
                {
                  "query": "example",
                  "max_results": 3
                }
                """);

        Assert.Contains(
            "UNTRUSTED WEB SEARCH RESULTS",
            output);

        Assert.Contains(
            "https://example.com/",
            output);
    }

    [Fact]
    public async Task WebCapability_FetchOutputLabelsContentUntrusted()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        capabilityRegistry.Register(
            new WebResearchCapability(
                ServiceWithResponse(
                    _ =>
                        TextResponse(
                            "<html><body>External text</body></html>",
                            "text/html"))),
            CapabilitySource.BuiltIn,
            SekoPermissionPolicy.CreateDefault(),
            toolRegistry);

        var output =
            await toolRegistry.ExecuteAsync(
                "web_fetch",
                """
                {
                  "url": "https://example.test/"
                }
                """);

        Assert.Contains(
            "UNTRUSTED WEB PAGE CONTENT",
            output);

        Assert.Contains(
            "External text",
            output);
    }

    [Theory]
    [InlineData("search the web for .NET 10 release notes")]
    [InlineData("what is the latest version of Blender?")]
    [InlineData("who is the current CEO of Example Corp?")]
    [InlineData("research this topic with sources")]
    [InlineData("what is the weather in Berlin today?")]
    public void WebIntentDetector_RecognizesResearchTasks(
        string request)
    {
        Assert.True(
            WebResearchIntentDetector.RequiresWebResearch(
                request));
    }

    [Theory]
    [InlineData("change Seko's current version")]
    [InlineData("read your latest task log")]
    [InlineData("fix this project")]
    public void WebIntentDetector_DoesNotHijackWorkspaceTasks(
        string request)
    {
        Assert.False(
            WebResearchIntentDetector.RequiresWebResearch(
                request));
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

    private static WebResearchService ServiceWithResponse(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client =
            new HttpClient(
                new StubHttpMessageHandler(
                    responder));

        return
            new WebResearchService(
                client,
                PublicGuard());
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

    private sealed class StubHttpMessageHandler :
        HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responder)
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
                Task.FromResult(
                    _responder(
                        request));
        }
    }
}
