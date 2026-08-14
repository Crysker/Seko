using System.Text.Json;
using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Capabilities.BuiltIn;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Tests.Agent;

public sealed class CapabilityRegistryRegressionTests
{
    [Fact]
    public void Register_AddsCapabilityAndItsTools()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var capability =
            CreateCapability(
                "example",
                new[]
                {
                    "example.read"
                },
                Tool(
                    "example_tool"));

        capabilityRegistry.Register(
            capability,
            toolRegistry);

        Assert.Same(
            capability,
            capabilityRegistry.FindById(
                "example"));

        Assert.Contains(
            "example_tool",
            toolRegistry.ToolNames);
    }

    [Fact]
    public void Register_DuplicateCapabilityIdIsRejected()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        capabilityRegistry.Register(
            CreateCapability(
                "same",
                new[]
                {
                    "first"
                }),
            toolRegistry);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    capabilityRegistry.Register(
                        CreateCapability(
                            "same",
                            new[]
                            {
                                "second"
                            }),
                        toolRegistry));

        Assert.Contains(
            "already registered",
            exception.Message);
    }

    [Fact]
    public void Register_ToolConflictIsRejectedBeforeCapabilityIsAdded()
    {
        var toolRegistry =
            new SekoToolRegistry();

        toolRegistry.Register(
            "shared_tool",
            Handler());

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    capabilityRegistry.Register(
                        CreateCapability(
                            "conflict",
                            new[]
                            {
                                "conflict.test"
                            },
                            Tool(
                                "shared_tool")),
                        toolRegistry));

        Assert.Contains(
            "already registered",
            exception.Message);

        Assert.Null(
            capabilityRegistry.FindById(
                "conflict"));
    }

    [Fact]
    public void Supports_IsProviderAgnosticAndCaseInsensitive()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        capabilityRegistry.Register(
            CreateCapability(
                "provider-a",
                new[]
                {
                    "design.edit"
                }),
            toolRegistry);

        Assert.True(
            capabilityRegistry.Supports(
                "DESIGN.EDIT"));

        Assert.False(
            capabilityRegistry.Supports(
                "design.export"));
    }

    [Fact]
    public void GetProviders_ReturnsMultipleProvidersForSameAbility()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var first =
            CreateCapability(
                "figma-like",
                new[]
                {
                    "design.edit"
                });

        var second =
            CreateCapability(
                "future-designer",
                new[]
                {
                    "design.edit"
                });

        capabilityRegistry.Register(
            first,
            toolRegistry);

        capabilityRegistry.Register(
            second,
            toolRegistry);

        var providers =
            capabilityRegistry.GetProviders(
                "design.edit");

        Assert.Equal(
            2,
            providers.Count);

        Assert.Contains(
            first,
            providers);

        Assert.Contains(
            second,
            providers);
    }

    [Fact]
    public void CapabilityDescriptor_RejectsDuplicateAbilities()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new CapabilityDescriptor(
                    "bad",
                    "Bad",
                    "Bad descriptor",
                    new[]
                    {
                        "same",
                        "SAME"
                    }));
    }

    [Fact]
    public void WorkspaceCapability_AdvertisesGenericFilesystemAbilities()
    {
        var capability =
            new WorkspaceCapability(
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler(),
                Handler());

        Assert.Contains(
            "filesystem.read",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "filesystem.write",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "workspace.search",
            capability.Descriptor.Abilities);
    }

    [Fact]
    public void BuildCapability_AdvertisesProjectBuildAbility()
    {
        var capability =
            new BuildCapability(
                Handler());

        Assert.Contains(
            "project.build",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "process.execute:dotnet",
            capability.Descriptor.RequiredPermissions);
    }

    [Fact]
    public void GitCapability_AdvertisesSourceControlAbilities()
    {
        var capability =
            new GitCapability(
                Handler(),
                Handler());

        Assert.Contains(
            "source.control.status",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "source.control.diff",
            capability.Descriptor.Abilities);

        Assert.Contains(
            "source.control.commit",
            capability.Descriptor.Abilities);
    }

    [Fact]
    public async Task RegisteredCapabilityTool_ExecutesThroughToolRegistry()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        capabilityRegistry.Register(
            CreateCapability(
                "echo-provider",
                new[]
                {
                    "example.echo"
                },
                new SekoToolRegistration(
                    "echo",
                    (arguments, _) =>
                        Task.FromResult(
                            arguments
                                .GetProperty(
                                    "value")
                                .GetString()
                            ?? string.Empty))),
            toolRegistry);

        var result =
            await toolRegistry.ExecuteAsync(
                "echo",
                """
                {
                  "value": "capability works"
                }
                """);

        Assert.Equal(
            "capability works",
            result);
    }

    private static ISekoCapability CreateCapability(
        string id,
        IEnumerable<string> abilities,
        params SekoToolRegistration[] tools)
    {
        return
            new TestCapability(
                new CapabilityDescriptor(
                    id,
                    id,
                    string.Empty,
                    abilities),
                tools);
    }

    private static SekoToolRegistration Tool(
        string name)
    {
        return
            new SekoToolRegistration(
                name,
                Handler());
    }

    private static SekoToolHandler Handler()
    {
        return
            (_, _) =>
                Task.FromResult(
                    "ok");
    }

    private sealed class TestCapability :
        ISekoCapability
    {
        public CapabilityDescriptor Descriptor
        {
            get;
        }

        public IReadOnlyCollection<SekoToolRegistration> Tools
        {
            get;
        }

        public TestCapability(
            CapabilityDescriptor descriptor,
            IReadOnlyCollection<SekoToolRegistration> tools)
        {
            Descriptor =
                descriptor;

            Tools =
                tools;
        }
    }
}
