using System.Text.Json.Nodes;

namespace Seko.Infrastructure.Agent;

public interface ISekoToolHost
{
    Task BeginTaskAsync(
        CancellationToken cancellationToken = default);

    JsonArray CreateToolDefinitions(
        IEnumerable<string> toolNames);

    Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default);

    string BuildAdaptiveContext(
        string currentTask);

    Task<string?> TryAutoCommitAsync(
        string userRequest,
        CancellationToken cancellationToken = default);
}