using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Capabilities.BuiltIn;
using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Tests.Agent;

public sealed class CapabilityRegistryRegressionTests
{
    [Fact]
    public void Register_AddsAllowedCapabilityAndItsTools()
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
                Array.Empty<string>(),
                Tool(
                    "example_tool"));

        var state =
            capabilityRegistry.Register(
                capability,
                CapabilitySource.Extension,
                AllowPolicy(),
                toolRegistry);

        Assert.Equal(
            CapabilityActivationState.Active,
            state);

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
            CapabilitySource.Extension,
            AllowPolicy(),
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
                        CapabilitySource.Extension,
                        AllowPolicy(),
                        toolRegistry));

        Assert.Contains(
            "already registered",
            exception.Message);
    }

    [Fact]
    public void Register_ActiveToolConflictIsRejectedBeforeCapabilityIsAdded()
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
                            Array.Empty<string>(),
                            Tool(
                                "shared_tool")),
                        CapabilitySource.Extension,
                        AllowPolicy(),
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
            CapabilitySource.Extension,
            AllowPolicy(),
            toolRegistry);

        Assert.True(
            capabilityRegistry.Supports(
                "DESIGN.EDIT"));

        Assert.False(
            capabilityRegistry.Supports(
                "design.export"));
    }

    [Fact]
    public void GetProviders_ReturnsMultipleActiveProvidersForSameAbility()
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
            CapabilitySource.Extension,
            AllowPolicy(),
            toolRegistry);

        capabilityRegistry.Register(
            second,
            CapabilitySource.Extension,
            AllowPolicy(),
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
    public async Task RegisteredAllowedCapabilityTool_ExecutesThroughToolRegistry()
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
                Array.Empty<string>(),
                new SekoToolRegistration(
                    "echo",
                    (arguments, _) =>
                        Task.FromResult(
                            arguments
                                .GetProperty(
                                    "value")
                                .GetString()
                            ?? string.Empty))),
            CapabilitySource.Extension,
            AllowPolicy(),
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

    [Fact]
    public void PendingCapability_IsKnownButDoesNotExposeToolsOrActiveAbility()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var capability =
            CreateCapability(
                "future-design",
                new[]
                {
                    "design.edit"
                },
                new[]
                {
                    "network"
                },
                Tool(
                    "future_design_edit"));

        var state =
            capabilityRegistry.Register(
                capability,
                CapabilitySource.Extension,
                SekoPermissionPolicy.CreateDefault(),
                toolRegistry);

        Assert.Equal(
            CapabilityActivationState.PendingApproval,
            state);

        Assert.True(
            capabilityRegistry.KnowsAbility(
                "design.edit"));

        Assert.False(
            capabilityRegistry.Supports(
                "design.edit"));

        Assert.Contains(
            capability,
            capabilityRegistry.GetKnownProviders(
                "design.edit"));

        Assert.DoesNotContain(
            "future_design_edit",
            toolRegistry.ToolNames);
    }

    [Fact]
    public void DeniedCapability_IsKnownButInactive()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var capability =
            CreateCapability(
                "unsafe",
                new[]
                {
                    "kernel.change"
                },
                new[]
                {
                    "self.modify.kernel"
                },
                Tool(
                    "unsafe_change"));

        var state =
            capabilityRegistry.Register(
                capability,
                CapabilitySource.BuiltIn,
                SekoPermissionPolicy.CreateDefault(),
                toolRegistry);

        Assert.Equal(
            CapabilityActivationState.Denied,
            state);

        Assert.Equal(
            CapabilityActivationState.Denied,
            capabilityRegistry.GetState(
                "unsafe"));

        Assert.False(
            capabilityRegistry.Supports(
                "kernel.change"));

        Assert.DoesNotContain(
            "unsafe_change",
            toolRegistry.ToolNames);
    }

    [Fact]
    public void BuiltInCapability_IsAllowedByDefaultPolicy()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var capability =
            CreateCapability(
                "trusted-built-in",
                new[]
                {
                    "example.trusted"
                },
                new[]
                {
                    "network",
                    "process.execute:anything"
                },
                Tool(
                    "trusted_tool"));

        var state =
            capabilityRegistry.Register(
                capability,
                CapabilitySource.BuiltIn,
                SekoPermissionPolicy.CreateDefault(),
                toolRegistry);

        Assert.Equal(
            CapabilityActivationState.Active,
            state);

        Assert.Contains(
            "trusted_tool",
            toolRegistry.ToolNames);
    }

    [Fact]
    public void PermissionEvaluation_IsStoredForCapability()
    {
        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        capabilityRegistry.Register(
            CreateCapability(
                "permission-test",
                new[]
                {
                    "example"
                },
                new[]
                {
                    "network"
                }),
            CapabilitySource.Extension,
            SekoPermissionPolicy.CreateDefault(),
            toolRegistry);

        var evaluation =
            capabilityRegistry.GetPermissionEvaluation(
                "permission-test");

        Assert.NotNull(
            evaluation);

        Assert.Equal(
            PermissionDecision.Ask,
            evaluation.OverallDecision);

        Assert.Equal(
            PermissionDecision.Ask,
            evaluation.GetDecision(
                "network"));
    }

    private static ISekoCapability CreateCapability(
        string id,
        IEnumerable<string> abilities,
        IEnumerable<string>? permissions = null,
        params SekoToolRegistration[] tools)
    {
        return
            new TestCapability(
                new CapabilityDescriptor(
                    id,
                    id,
                    string.Empty,
                    abilities,
                    permissions),
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

    private static SekoPermissionPolicy AllowPolicy()
    {
        return
            new SekoPermissionPolicy(
                defaultDecision:
                    PermissionDecision.Allow);
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
