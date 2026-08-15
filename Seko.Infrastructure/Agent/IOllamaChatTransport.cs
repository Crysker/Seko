using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seko.Infrastructure.Agent;

public interface IOllamaChatTransport
{
    Task<JsonDocument> SendAsync(
        JsonObject request,
        CancellationToken cancellationToken = default);
}