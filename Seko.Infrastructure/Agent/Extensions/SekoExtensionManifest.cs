namespace Seko.Infrastructure.Agent.Extensions;

public sealed class SekoExtensionManifest
{
    public int SchemaVersion
    {
        get;
        set;
    } =
        1;

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

    public string Version
    {
        get;
        set;
    } =
        "1.0.0";

    public string Description
    {
        get;
        set;
    } =
        string.Empty;

    public string Runtime
    {
        get;
        set;
    } =
        "declarative-v1";

    public List<string> Abilities
    {
        get;
        set;
    } =
        new();

    public List<string> Permissions
    {
        get;
        set;
    } =
        new();

    public List<SekoExtensionSkillManifest> Skills
    {
        get;
        set;
    } =
        new();
}
