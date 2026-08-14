using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Permissions;
using Seko.Infrastructure.Agent.Tools;

namespace Seko.Tests.Agent;

public sealed class PermissionPersistenceRegressionTests
{
    [Fact]
    public async Task PermissionStore_RoundTripsExactDecisions()
    {
        using var scope =
            new TemporaryDirectory();

        var path =
            Path.Combine(
                scope.Path,
                "permissions.json");

        var store =
            new SekoPermissionStore(
                path);

        await store.SaveAsync(
            new[]
            {
                new PermissionPreference(
                    "figma",
                    CapabilitySource.Extension,
                    "network",
                    PermissionDecision.Allow)
            });

        var loaded =
            store.Load();

        var preference =
            Assert.Single(
                loaded);

        Assert.Equal(
            "figma",
            preference.CapabilityId);

        Assert.Equal(
            CapabilitySource.Extension,
            preference.Source);

        Assert.Equal(
            "network",
            preference.Permission);

        Assert.Equal(
            PermissionDecision.Allow,
            preference.Decision);
    }

    [Fact]
    public async Task PermissionManager_PersistsDecisionAcrossReload()
    {
        using var scope =
            new TemporaryDirectory();

        var store =
            new SekoPermissionStore(
                Path.Combine(
                    scope.Path,
                    "permissions.json"));

        var manager =
            SekoPermissionManager.Load(
                store);

        await manager.SetDecisionAsync(
            "figma",
            CapabilitySource.Extension,
            "network",
            PermissionDecision.Allow);

        var reloaded =
            SekoPermissionManager.Load(
                store);

        Assert.Equal(
            PermissionDecision.Allow,
            reloaded.Policy.Evaluate(
                "figma",
                CapabilitySource.Extension,
                "network"));

        Assert.Equal(
            PermissionDecision.Ask,
            reloaded.Policy.Evaluate(
                "another-extension",
                CapabilitySource.Extension,
                "network"));
    }

    [Fact]
    public async Task PermissionManager_AskRemovesPersistedOverride()
    {
        using var scope =
            new TemporaryDirectory();

        var store =
            new SekoPermissionStore(
                Path.Combine(
                    scope.Path,
                    "permissions.json"));

        var manager =
            SekoPermissionManager.Load(
                store);

        await manager.SetDecisionAsync(
            "figma",
            CapabilitySource.Extension,
            "network",
            PermissionDecision.Allow);

        await manager.SetDecisionAsync(
            "figma",
            CapabilitySource.Extension,
            "network",
            PermissionDecision.Ask);

        var reloaded =
            SekoPermissionManager.Load(
                store);

        Assert.Empty(
            reloaded.Preferences);

        Assert.Equal(
            PermissionDecision.Ask,
            reloaded.Policy.Evaluate(
                "figma",
                CapabilitySource.Extension,
                "network"));
    }

    [Fact]
    public void PermissionManager_CorruptStoreFailsClosed()
    {
        using var scope =
            new TemporaryDirectory();

        var path =
            Path.Combine(
                scope.Path,
                "permissions.json");

        File.WriteAllText(
            path,
            "{ definitely not valid json");

        var manager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    path));

        Assert.NotNull(
            manager.LoadWarning);

        Assert.Empty(
            manager.Preferences);

        Assert.Equal(
            PermissionDecision.Ask,
            manager.Policy.Evaluate(
                "figma",
                CapabilitySource.Extension,
                "network"));
    }

    [Fact]
    public async Task ProtectedPermission_CannotBeOverriddenByPersistedAllow()
    {
        using var scope =
            new TemporaryDirectory();

        var store =
            new SekoPermissionStore(
                Path.Combine(
                    scope.Path,
                    "permissions.json"));

        var manager =
            SekoPermissionManager.Load(
                store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                manager.SetDecisionAsync(
                    "unsafe-extension",
                    CapabilitySource.Extension,
                    "self.modify.kernel",
                    PermissionDecision.Allow));

        Assert.Equal(
            PermissionDecision.Deny,
            manager.Policy.Evaluate(
                "unsafe-extension",
                CapabilitySource.Extension,
                "self.modify.kernel"));

        Assert.Empty(
            manager.Preferences);
    }

    [Fact]
    public void CapabilitySpecificRule_DoesNotGrantOtherCapability()
    {
        var policy =
            SekoPermissionPolicy.CreateDefault(
                new[]
                {
                    new PermissionRule(
                        "figma",
                        CapabilitySource.Extension,
                        "network",
                        PermissionDecision.Allow)
                });

        Assert.Equal(
            PermissionDecision.Allow,
            policy.Evaluate(
                "figma",
                CapabilitySource.Extension,
                "network"));

        Assert.Equal(
            PermissionDecision.Ask,
            policy.Evaluate(
                "other",
                CapabilitySource.Extension,
                "network"));
    }

    [Fact]
    public async Task ApprovalService_ActivatesPendingCapabilityImmediately()
    {
        using var scope =
            new TemporaryDirectory();

        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var manager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    Path.Combine(
                        scope.Path,
                        "permissions.json")));

        var capability =
            new TestCapability(
                "figma-like",
                "design.edit",
                "network",
                "design_edit");

        var initialState =
            capabilityRegistry.Register(
                capability,
                CapabilitySource.Extension,
                manager.Policy,
                toolRegistry);

        Assert.Equal(
            CapabilityActivationState.PendingApproval,
            initialState);

        Assert.DoesNotContain(
            "design_edit",
            toolRegistry.ToolNames);

        var service =
            new SekoCapabilityPermissionService(
                manager,
                capabilityRegistry,
                toolRegistry);

        var state =
            await service.SetDecisionAsync(
                "figma-like",
                "network",
                PermissionDecision.Allow);

        Assert.Equal(
            CapabilityActivationState.Active,
            state);

        Assert.Contains(
            "design_edit",
            toolRegistry.ToolNames);

        Assert.True(
            capabilityRegistry.Supports(
                "design.edit"));
    }

    [Fact]
    public async Task ApprovalService_RevokesActiveCapabilityImmediately()
    {
        using var scope =
            new TemporaryDirectory();

        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var manager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    Path.Combine(
                        scope.Path,
                        "permissions.json")));

        await manager.SetDecisionAsync(
            "figma-like",
            CapabilitySource.Extension,
            "network",
            PermissionDecision.Allow);

        var capability =
            new TestCapability(
                "figma-like",
                "design.edit",
                "network",
                "design_edit");

        var initialState =
            capabilityRegistry.Register(
                capability,
                CapabilitySource.Extension,
                manager.Policy,
                toolRegistry);

        Assert.Equal(
            CapabilityActivationState.Active,
            initialState);

        var service =
            new SekoCapabilityPermissionService(
                manager,
                capabilityRegistry,
                toolRegistry);

        var state =
            await service.SetDecisionAsync(
                "figma-like",
                "network",
                PermissionDecision.Ask);

        Assert.Equal(
            CapabilityActivationState.PendingApproval,
            state);

        Assert.DoesNotContain(
            "design_edit",
            toolRegistry.ToolNames);

        Assert.False(
            capabilityRegistry.Supports(
                "design.edit"));
    }

    [Fact]
    public async Task ApprovalService_DeniesCapabilityAndRemovesItsTools()
    {
        using var scope =
            new TemporaryDirectory();

        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var manager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    Path.Combine(
                        scope.Path,
                        "permissions.json")));

        await manager.SetDecisionAsync(
            "figma-like",
            CapabilitySource.Extension,
            "network",
            PermissionDecision.Allow);

        var capability =
            new TestCapability(
                "figma-like",
                "design.edit",
                "network",
                "design_edit");

        capabilityRegistry.Register(
            capability,
            CapabilitySource.Extension,
            manager.Policy,
            toolRegistry);

        var service =
            new SekoCapabilityPermissionService(
                manager,
                capabilityRegistry,
                toolRegistry);

        var state =
            await service.SetDecisionAsync(
                "figma-like",
                "network",
                PermissionDecision.Deny);

        Assert.Equal(
            CapabilityActivationState.Denied,
            state);

        Assert.DoesNotContain(
            "design_edit",
            toolRegistry.ToolNames);
    }

    [Fact]
    public async Task ApprovalService_RejectsPermissionCapabilityDidNotRequest()
    {
        using var scope =
            new TemporaryDirectory();

        var toolRegistry =
            new SekoToolRegistry();

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var manager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    Path.Combine(
                        scope.Path,
                        "permissions.json")));

        capabilityRegistry.Register(
            new TestCapability(
                "figma-like",
                "design.edit",
                "network",
                "design_edit"),
            CapabilitySource.Extension,
            manager.Policy,
            toolRegistry);

        var service =
            new SekoCapabilityPermissionService(
                manager,
                capabilityRegistry,
                toolRegistry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.SetDecisionAsync(
                    "figma-like",
                    "process.execute:anything",
                    PermissionDecision.Allow));
    }

    [Fact]
    public async Task ApprovalService_ActivationConflictRollsBackPersistedGrant()
    {
        using var scope =
            new TemporaryDirectory();

        var toolRegistry =
            new SekoToolRegistry();

        toolRegistry.Register(
            "design_edit",
            (_, _) =>
                Task.FromResult(
                    "existing"));

        var capabilityRegistry =
            new SekoCapabilityRegistry();

        var manager =
            SekoPermissionManager.Load(
                new SekoPermissionStore(
                    Path.Combine(
                        scope.Path,
                        "permissions.json")));

        capabilityRegistry.Register(
            new TestCapability(
                "figma-like",
                "design.edit",
                "network",
                "design_edit"),
            CapabilitySource.Extension,
            manager.Policy,
            toolRegistry);

        var service =
            new SekoCapabilityPermissionService(
                manager,
                capabilityRegistry,
                toolRegistry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.SetDecisionAsync(
                    "figma-like",
                    "network",
                    PermissionDecision.Allow));

        Assert.Equal(
            PermissionDecision.Ask,
            manager.Policy.Evaluate(
                "figma-like",
                CapabilitySource.Extension,
                "network"));

        Assert.Empty(
            manager.Preferences);

        Assert.Equal(
            CapabilityActivationState.PendingApproval,
            capabilityRegistry.GetState(
                "figma-like"));
    }

    [Fact]
    public void ToolRegistry_UnregisterRemovesTool()
    {
        var registry =
            new SekoToolRegistry();

        registry.Register(
            "temporary",
            (_, _) =>
                Task.FromResult(
                    "ok"));

        Assert.True(
            registry.Unregister(
                "temporary"));

        Assert.DoesNotContain(
            "temporary",
            registry.ToolNames);

        Assert.False(
            registry.Unregister(
                "temporary"));
    }

    private sealed class TestCapability :
        ISekoCapability
    {
        private readonly IReadOnlyCollection<SekoToolRegistration> _tools;

        public CapabilityDescriptor Descriptor
        {
            get;
        }

        public IReadOnlyCollection<SekoToolRegistration> Tools =>
            _tools;

        public TestCapability(
            string id,
            string ability,
            string permission,
            string toolName)
        {
            Descriptor =
                new CapabilityDescriptor(
                    id,
                    id,
                    string.Empty,
                    new[]
                    {
                        ability
                    },
                    new[]
                    {
                        permission
                    });

            _tools =
                new[]
                {
                    new SekoToolRegistration(
                        toolName,
                        (_, _) =>
                            Task.FromResult(
                                "ok"))
                };
        }
    }

    private sealed class TemporaryDirectory :
        IDisposable
    {
        public string Path
        {
            get;
        }

        public TemporaryDirectory()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "SekoTests",
                    Guid.NewGuid().ToString(
                        "N"));

            Directory.CreateDirectory(
                Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(
                        Path))
                {
                    Directory.Delete(
                        Path,
                        true);
                }
            }
            catch
            {
                // Best effort test cleanup.
            }
        }
    }
}
