namespace Seko.Infrastructure.Agent.Extensions;

public sealed class SekoExtensionSkillManifest
{
    public string Id
    {
        get;
        set;
    } =
        string.Empty;

    public string Name
    {
        get;
        set;
    } =
        string.Empty;

    public string Description
    {
        get;
        set;
    } =
        string.Empty;

    public List<string> TriggerTerms
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

    public List<string> PreferredAbilities
    {
        get;
        set;
    } =
        new();

    public string Instructions
    {
        get;
        set;
    } =
        string.Empty;

    public int Priority
    {
        get;
        set;
    }
}
