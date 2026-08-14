using System.Text.Json;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Tests.Agent;

public sealed class SekoToolRegistryRegressionTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesArgumentsToRegisteredHandler()
    {
        var registry =
            new SekoToolRegistry();

        registry.Register(
            "echo",
            (arguments, _) =>
                Task.FromResult(
                    arguments
                        .GetProperty(
                            "value")
                        .GetString()
                    ?? string.Empty));

        var result =
            await registry.ExecuteAsync(
                "echo",
                """
                {
                  "value": "hello"
                }
                """);

        Assert.Equal(
            "hello",
            result);
    }

    [Fact]
    public async Task ExecuteAsync_BlankArgumentsBecomeEmptyObject()
    {
        var registry =
            new SekoToolRegistry();

        registry.Register(
            "empty",
            (arguments, _) =>
                Task.FromResult(
                    arguments.ValueKind
                    == JsonValueKind.Object
                    && !arguments.EnumerateObject().Any()
                        ? "empty"
                        : "not-empty"));

        var result =
            await registry.ExecuteAsync(
                "empty",
                "   ");

        Assert.Equal(
            "empty",
            result);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownToolPreservesExistingErrorShape()
    {
        var registry =
            new SekoToolRegistry();

        var result =
            await registry.ExecuteAsync(
                "missing_tool",
                "{}");

        Assert.Equal(
            "ERROR: Unknown tool 'missing_tool'.",
            result);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerExceptionIsFormattedAsToolError()
    {
        var registry =
            new SekoToolRegistry();

        registry.Register(
            "fail",
            (_, _) =>
                throw new InvalidOperationException(
                    "boom"));

        var result =
            await registry.ExecuteAsync(
                "fail",
                "{}");

        Assert.Equal(
            "ERROR: InvalidOperationException: boom",
            result);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationIsRethrown()
    {
        var registry =
            new SekoToolRegistry();

        registry.Register(
            "cancel",
            (_, cancellationToken) =>
                Task.FromCanceled<string>(
                    cancellationToken));

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                registry.ExecuteAsync(
                    "cancel",
                    "{}",
                    cancellationSource.Token));
    }

    [Fact]
    public void Register_DuplicateToolNameIsRejected()
    {
        var registry =
            new SekoToolRegistry();

        registry.Register(
            "same",
            (_, _) =>
                Task.FromResult(
                    "first"));

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    registry.Register(
                        "same",
                        (_, _) =>
                            Task.FromResult(
                                "second")));

        Assert.Contains(
            "already registered",
            exception.Message);
    }

    [Fact]
    public void ToolNames_PreserveExactCaseSensitiveRegistration()
    {
        var registry =
            new SekoToolRegistry();

        registry.Register(
            "git_status",
            (_, _) =>
                Task.FromResult(
                    "ok"));

        Assert.Contains(
            "git_status",
            registry.ToolNames);

        Assert.DoesNotContain(
            "GIT_STATUS",
            registry.ToolNames);
    }
}
