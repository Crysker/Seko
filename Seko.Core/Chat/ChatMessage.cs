namespace Seko.Core.Chat;

public sealed class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public MessageRole Role { get; init; }

    public required string Content { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}