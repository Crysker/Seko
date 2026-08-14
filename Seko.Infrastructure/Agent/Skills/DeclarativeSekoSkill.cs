namespace Seko.Infrastructure.Agent.Skills;

public sealed class DeclarativeSekoSkill :
    ISekoSkill
{
    public SekoSkillDescriptor Descriptor
    {
        get;
    }

    public DeclarativeSekoSkill(
        SekoSkillDescriptor descriptor)
    {
        Descriptor =
            descriptor
            ?? throw new ArgumentNullException(
                nameof(descriptor));
    }
}
