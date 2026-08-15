namespace Seko.Infrastructure.Attachments;

public enum SekoAttachmentKind
{
    Text,
    Image
}

public sealed record SekoAttachment(
    string FilePath,
    string DisplayName,
    SekoAttachmentKind Kind)
{
    public string KindDisplay =>
        Kind == SekoAttachmentKind.Image
            ? "Image"
            : "File";
}