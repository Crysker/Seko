using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Seko.Infrastructure.Agent;

public static class SekoVerificationPolicy
{
    public static bool RequiresBuild(
        string relativePath)
    {
        var extension =
            Path.GetExtension(
                relativePath);

        return extension.Equals(
                   ".cs",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".xaml",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".csproj",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".sln",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".props",
                   StringComparison.OrdinalIgnoreCase)
               || extension.Equals(
                   ".targets",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string GetStructureKind(
        string relativePath,
        string content)
    {
        var extension =
            Path.GetExtension(
                relativePath)
                .ToLowerInvariant();

        return extension switch
        {
            ".json" =>
                "json",

            ".jsonc" =>
                "jsonc",

            ".xml" =>
                "xml",

            ".config" when content
                .TrimStart()
                .StartsWith(
                    "<",
                    StringComparison.Ordinal) =>
                "xml",

            _ =>
                "text"
        };
    }

    public static string? ValidateStructure(
        string relativePath,
        string content)
    {
        var structureKind =
            GetStructureKind(
                relativePath,
                content);

        try
        {
            switch (structureKind)
            {
                case "json":
                    using (JsonDocument.Parse(
                               content))
                    {
                    }

                    break;

                case "jsonc":
                    using (JsonDocument.Parse(
                               content,
                               new JsonDocumentOptions
                               {
                                   AllowTrailingCommas =
                                       true,

                                   CommentHandling =
                                       JsonCommentHandling.Skip
                               }))
                    {
                    }

                    break;

                case "xml":
                    _ =
                        XDocument.Parse(
                            content,
                            LoadOptions.PreserveWhitespace
                            | LoadOptions.SetLineInfo);

                    break;
            }

            return null;
        }
        catch (JsonException exception)
        {
            return
                "Malformed JSON: "
                + exception.Message;
        }
        catch (XmlException exception)
        {
            return
                "Malformed XML: "
                + exception.Message;
        }
    }
}