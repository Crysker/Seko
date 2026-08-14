namespace Seko.Infrastructure.Agent.Tools;

public sealed record SekoToolRegistration(
    string Name,
    SekoToolHandler Handler);
