using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seko.Infrastructure.Agent;

public sealed class OllamaChatTransport :
    IOllamaChatTransport
{
    private static readonly HttpClient HttpClient =
        new()
        {
            BaseAddress =
                new Uri(
                    "http://localhost:11434"),

            Timeout =
                TimeSpan.FromMinutes(
                    5)
        };

    public async Task<JsonDocument> SendAsync(
        JsonObject request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        HttpResponseMessage response;

        try
        {
            response =
                await HttpClient.PostAsJsonAsync(
                    "/api/chat",
                    request,
                    cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The Ollama request timed out before the model responded. " +
                "The task failed rather than being treated as a user Stop action.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "I couldn't connect to Ollama. " +
                "Make sure Ollama is running and qwen3:8b is installed.\n\n" +
                exception.Message);
        }

        using (response)
        {
            var responseText =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Ollama returned HTTP {(int)response.StatusCode}.\n\n" +
                    responseText);
            }

            return JsonDocument.Parse(
                responseText);
        }
    }
}