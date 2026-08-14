using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities.BuiltIn;

public sealed class BuildCapability :
    ISekoCapability
{
    private readonly IReadOnlyCollection<SekoToolRegistration> _tools;

    public CapabilityDescriptor Descriptor
    {
        get;
    } =
        new(
            "build.dotnet",
            ".NET Build",
            "Build and verify the active .NET workspace.",
            new[]
            {
                "project.build",
                "project.verify"
            },
            new[]
            {
                "process.execute:dotnet",
                "filesystem.write:build-output"
            });

    public IReadOnlyCollection<SekoToolRegistration> Tools =>
        _tools;

    public BuildCapability(
        SekoToolHandler buildProject)
    {
        _tools =
            new[]
            {
                new SekoToolRegistration(
                    "build_project",
                    buildProject)
            };
    }
}
