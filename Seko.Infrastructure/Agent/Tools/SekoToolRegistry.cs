using System.Text.Json;

namespace Seko.Infrastructure.Agent.Tools;

public sealed class SekoToolRegistry
{
    private readonly Dictionary<string, SekoToolHandler> _handlers =
        new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ToolNames =>
        _handlers.Keys;

    public void Register(
        string toolName,
        SekoToolHandler handler)
    {
        if (string.IsNullOrWhiteSpace(
                toolName))
        {
            throw new ArgumentException(
                "Tool name cannot be empty.",
                nameof(toolName));
        }

        ArgumentNullException.ThrowIfNull(
            handler);

        if (!_handlers.TryAdd(
                toolName,
                handler))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' is already registered.");
        }
    }

    public async Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(
                        argumentsJson)
                        ? "{}"
                        : argumentsJson);

            var arguments =
                document.RootElement;

            if (!_handlers.TryGetValue(
                    toolName,
                    out var handler))
            {
                return
                    $"ERROR: Unknown tool '{toolName}'.";
            }

            return
                await handler(
                    arguments,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return
                $"ERROR: {exception.GetType().Name}: "
                + exception.Message;
        }
    }
}
