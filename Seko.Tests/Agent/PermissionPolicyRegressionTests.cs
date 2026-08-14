using Seko.Infrastructure.Agent.Permissions;

namespace Seko.Tests.Agent;

public sealed class PermissionPolicyRegressionTests
{
    [Fact]
    public void DefaultPolicy_AllowsBuiltInCapabilityPermissions()
    {
        var policy =
            SekoPermissionPolicy.CreateDefault();

        Assert.Equal(
            PermissionDecision.Allow,
            policy.Evaluate(
                CapabilitySource.BuiltIn,
                "filesystem.write"));
    }

    [Fact]
    public void DefaultPolicy_AsksForUnknownExtensionPermission()
    {
        var policy =
            SekoPermissionPolicy.CreateDefault();

        Assert.Equal(
            PermissionDecision.Ask,
            policy.Evaluate(
                CapabilitySource.Extension,
                "network"));
    }

    [Fact]
    public void DefaultPolicy_AsksForUnknownProjectPermission()
    {
        var policy =
            SekoPermissionPolicy.CreateDefault();

        Assert.Equal(
            PermissionDecision.Ask,
            policy.Evaluate(
                CapabilitySource.Project,
                "process.execute:custom-tool"));
    }

    [Fact]
    public void DefaultPolicy_DeniesKernelModificationEvenForBuiltInCapability()
    {
        var policy =
            SekoPermissionPolicy.CreateDefault();

        Assert.Equal(
            PermissionDecision.Deny,
            policy.Evaluate(
                CapabilitySource.BuiltIn,
                "self.modify.kernel"));
    }

    [Fact]
    public void ExactRule_BeatsWildcardRule()
    {
        var policy =
            new SekoPermissionPolicy(
                new[]
                {
                    new PermissionRule(
                        null,
                        "network*",
                        PermissionDecision.Allow),

                    new PermissionRule(
                        null,
                        "network.secret",
                        PermissionDecision.Deny)
                });

        Assert.Equal(
            PermissionDecision.Deny,
            policy.Evaluate(
                CapabilitySource.Extension,
                "network.secret"));
    }

    [Fact]
    public void LongerWildcard_BeatsShorterWildcard()
    {
        var policy =
            new SekoPermissionPolicy(
                new[]
                {
                    new PermissionRule(
                        null,
                        "filesystem.*",
                        PermissionDecision.Ask),

                    new PermissionRule(
                        null,
                        "filesystem.read*",
                        PermissionDecision.Allow)
                });

        Assert.Equal(
            PermissionDecision.Allow,
            policy.Evaluate(
                CapabilitySource.Extension,
                "filesystem.read:project"));
    }

    [Fact]
    public void SourceSpecificRule_BeatsSourceAgnosticRuleAtSameSpecificity()
    {
        var policy =
            new SekoPermissionPolicy(
                new[]
                {
                    new PermissionRule(
                        null,
                        "network",
                        PermissionDecision.Ask),

                    new PermissionRule(
                        CapabilitySource.Project,
                        "network",
                        PermissionDecision.Deny)
                });

        Assert.Equal(
            PermissionDecision.Deny,
            policy.Evaluate(
                CapabilitySource.Project,
                "network"));

        Assert.Equal(
            PermissionDecision.Ask,
            policy.Evaluate(
                CapabilitySource.Extension,
                "network"));
    }

    [Fact]
    public void LaterEquivalentRule_Wins()
    {
        var policy =
            new SekoPermissionPolicy(
                new[]
                {
                    new PermissionRule(
                        CapabilitySource.Extension,
                        "network",
                        PermissionDecision.Ask),

                    new PermissionRule(
                        CapabilitySource.Extension,
                        "network",
                        PermissionDecision.Allow)
                });

        Assert.Equal(
            PermissionDecision.Allow,
            policy.Evaluate(
                CapabilitySource.Extension,
                "network"));
    }

    [Fact]
    public void Evaluation_UsesDenyAsStrongestOverallDecision()
    {
        var policy =
            new SekoPermissionPolicy(
                new[]
                {
                    new PermissionRule(
                        null,
                        "safe",
                        PermissionDecision.Allow),

                    new PermissionRule(
                        null,
                        "danger",
                        PermissionDecision.Deny)
                },
                PermissionDecision.Ask);

        var evaluation =
            policy.Evaluate(
                new PermissionRequest(
                    "test",
                    CapabilitySource.Extension,
                    new[]
                    {
                        "safe",
                        "unknown",
                        "danger"
                    }));

        Assert.Equal(
            PermissionDecision.Deny,
            evaluation.OverallDecision);

        Assert.Equal(
            PermissionDecision.Allow,
            evaluation.GetDecision(
                "safe"));

        Assert.Equal(
            PermissionDecision.Ask,
            evaluation.GetDecision(
                "unknown"));

        Assert.Equal(
            PermissionDecision.Deny,
            evaluation.GetDecision(
                "danger"));
    }

    [Fact]
    public void Evaluation_UsesAskWhenNothingIsDeniedButApprovalIsNeeded()
    {
        var policy =
            new SekoPermissionPolicy(
                new[]
                {
                    new PermissionRule(
                        null,
                        "safe",
                        PermissionDecision.Allow)
                },
                PermissionDecision.Ask);

        var evaluation =
            policy.Evaluate(
                new PermissionRequest(
                    "test",
                    CapabilitySource.Extension,
                    new[]
                    {
                        "safe",
                        "unknown"
                    }));

        Assert.Equal(
            PermissionDecision.Ask,
            evaluation.OverallDecision);

        Assert.True(
            evaluation.RequiresApproval);
    }

    [Fact]
    public void EmptyPermissionSet_IsAllowed()
    {
        var policy =
            SekoPermissionPolicy.CreateDefault();

        var evaluation =
            policy.Evaluate(
                new PermissionRequest(
                    "no-permissions",
                    CapabilitySource.Extension,
                    Array.Empty<string>()));

        Assert.Equal(
            PermissionDecision.Allow,
            evaluation.OverallDecision);

        Assert.True(
            evaluation.IsAllowed);
    }

    [Fact]
    public void PermissionRule_RejectsNonTerminalWildcard()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new PermissionRule(
                    null,
                    "file*system",
                    PermissionDecision.Allow));
    }

    [Fact]
    public void PermissionRequest_RejectsDuplicatePermissionsIgnoringCase()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new PermissionRequest(
                    "duplicate",
                    CapabilitySource.Extension,
                    new[]
                    {
                        "network",
                        "NETWORK"
                    }));
    }
}
