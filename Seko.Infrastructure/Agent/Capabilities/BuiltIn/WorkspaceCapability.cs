using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities.BuiltIn;

public sealed class WorkspaceCapability :
    ISekoCapability
{
    private readonly IReadOnlyCollection<SekoToolRegistration> _tools;

    public CapabilityDescriptor Descriptor
    {
        get;
    } =
        new(
            "workspace",
            "Workspace",
            "Inspect, search, read and safely modify files in the active project workspace.",
            new[]
            {
                "workspace.search",
                "filesystem.inspect",
                "filesystem.read",
                "filesystem.write",
                "diagnostics.read"
            },
            new[]
            {
                "filesystem.read",
                "filesystem.write",
                "diagnostics.read"
            });

    public IReadOnlyCollection<SekoToolRegistration> Tools =>
        _tools;

    public WorkspaceCapability(
        SekoToolHandler searchWorkspace,
        SekoToolHandler findFiles,
        SekoToolHandler findText,
        SekoToolHandler listFiles,
        SekoToolHandler readFile,
        SekoToolHandler readTaskLog,
        SekoToolHandler writeFile,
        SekoToolHandler replaceText)
    {
        _tools =
            new[]
            {
                new SekoToolRegistration(
                    "search_workspace",
                    searchWorkspace),

                new SekoToolRegistration(
                    "find_files",
                    findFiles),

                new SekoToolRegistration(
                    "find_text",
                    findText),

                new SekoToolRegistration(
                    "list_files",
                    listFiles),

                new SekoToolRegistration(
                    "read_file",
                    readFile),

                new SekoToolRegistration(
                    "read_task_log",
                    readTaskLog),

                new SekoToolRegistration(
                    "write_file",
                    writeFile),

                new SekoToolRegistration(
                    "replace_text",
                    replaceText)
            };
    }
}
