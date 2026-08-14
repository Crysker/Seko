using System.Text.Json;

namespace Seko.Infrastructure.Agent.Tools;

public delegate Task<string> SekoToolHandler(
    JsonElement arguments,
    CancellationToken cancellationToken);
