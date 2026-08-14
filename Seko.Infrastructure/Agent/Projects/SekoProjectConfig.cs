namespace Seko.Infrastructure.Agent.Projects;

public sealed class SekoProjectConfig
{
    public int Version
    {
        get;
        set;
    } =
        1;

    public string? Name
    {
        get;
        set;
    }

    public string? Type
    {
        get;
        set;
    }

    public List<string> Technologies
    {
        get;
        set;
    } =
        new();

    public List<string> RequiredAbilities
    {
        get;
        set;
    } =
        new();

    public List<string> PreferredCapabilities
    {
        get;
        set;
    } =
        new();

    public List<string> EnabledSkills
    {
        get;
        set;
    } =
        new();
}
