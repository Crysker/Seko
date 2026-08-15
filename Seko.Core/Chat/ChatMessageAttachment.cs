namespace Seko.Core.Chat;

public enum ChatMessageAttachmentKind
{
    File,
    Image
}

public sealed record ChatMessageAttachment(
    string FilePath,
    string DisplayName,
    ChatMessageAttachmentKind Kind);