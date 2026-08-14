using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities;

public interface ISekoCapability
{
    CapabilityDescriptor Descriptor
    {
        get;
    }

    IReadOnlyCollection<SekoToolRegistration> Tools
    {
        get;
    }
}
