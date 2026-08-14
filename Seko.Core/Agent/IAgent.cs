using Seko.Core.Chat;

namespace Seko.Core.Agent;

public interface IAgent
{
    Task<ChatMessage> SendAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default);
}