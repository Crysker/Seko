using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities.BuiltIn;

public sealed class GitCapability :
    ISekoCapability
{
    private readonly IReadOnlyCollection<SekoToolRegistration> _tools;

    public CapabilityDescriptor Descriptor
    {
        get;
    } =
        new(
            "source-control.git",
            "Git",
            "Inspect and manage source-control state for a Git workspace.",
            new[]
            {
                "source.control.status",
                "source.control.diff",
                "source.control.commit"
            },
            new[]
            {
                "process.execute:git",
                "filesystem.read",
                "filesystem.write:git"
            });

    public IReadOnlyCollection<SekoToolRegistration> Tools =>
        _tools;

    public GitCapability(
        SekoToolHandler gitStatus,
        SekoToolHandler gitDiff)
    {
        _tools =
            new[]
            {
                new SekoToolRegistration(
                    "git_status",
                    gitStatus),

                new SekoToolRegistration(
                    "git_diff",
                    gitDiff)
            };
    }
}
