using Seko.Infrastructure.Agent.Tools;

namespace Seko.Infrastructure.Agent.Capabilities.BuiltIn;

public sealed class ProductIdentityCapability :
    ISekoCapability
{
    private readonly IReadOnlyCollection<SekoToolRegistration> _tools;

    public CapabilityDescriptor Descriptor
    {
        get;
    } =
        new(
            "product.identity",
            "Product Identity",
            "Inspect and deterministically verify Seko's canonical product identity.",
            new[]
            {
                "product.identity.inspect",
                "product.identity.verify",
                "project.test"
            },
            new[]
            {
                "filesystem.read",
                "process.execute:dotnet"
            });

    public IReadOnlyCollection<SekoToolRegistration> Tools =>
        _tools;

    public ProductIdentityCapability(
        SekoToolHandler inspectProductIdentity,
        SekoToolHandler testProject,
        SekoToolHandler verifyProductIdentity)
    {
        _tools =
            new[]
            {
                new SekoToolRegistration(
                    "inspect_product_identity",
                    inspectProductIdentity),

                new SekoToolRegistration(
                    "test_project",
                    testProject),

                new SekoToolRegistration(
                    "verify_product_identity",
                    verifyProductIdentity)
            };
    }
}