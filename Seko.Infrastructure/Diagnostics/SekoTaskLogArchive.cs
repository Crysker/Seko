using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Seko.Infrastructure.Diagnostics;

public sealed class SekoTaskLogArchive
{
    private const int MaximumPrefixCharacters =
        32_000;

    private const int MaximumRequestPreviewLength =
        180;

    private static readonly Regex WhitespaceRegex =
        new(
            @"\s+",
            RegexOptions.Compiled);

    private readonly string _logDirectory;

    public SekoTaskLogArchive()
        : this(
            null)
    {
    }

    public SekoTaskLogArchive(
        string? logDirectory)
    {
        if (!string.IsNullOrWhiteSpace(
                logDirectory))
        {
            _logDirectory =
                Path.GetFullPath(
                    logDirectory);

            return;
        }

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        _logDirectory =
            Path.Combine(
                localAppData,
                "Seko",
                "Logs",
                "Tasks");
    }

    public string LogDirectory =>
        _logDirectory;

    public IReadOnlyList<SekoTaskLogSummary> LoadRecent(
        int maximumCount = 100)
    {
        if (maximumCount <= 0)
        {
            return
                Array.Empty<SekoTaskLogSummary>();
        }

        try
        {
            if (!Directory.Exists(
                    _logDirectory))
            {
                return
                    Array.Empty<SekoTaskLogSummary>();
            }

            var candidates =
                Directory
                    .EnumerateFiles(
                        _logDirectory,
                        "*.md",
                        SearchOption.TopDirectoryOnly)
                    .Select(
                        path =>
                            new FileInfo(
                                path))
                    .OrderByDescending(
                        file =>
                            file.LastWriteTimeUtc)
                    .Take(
                        Math.Max(
                            maximumCount * 3,
                            maximumCount))
                    .ToArray();

            var summaries =
                new List<SekoTaskLogSummary>();

            foreach (var file
                     in candidates)
            {
                try
                {
                    var prefix =
                        ReadPrefix(
                            file.FullName);

                    var fallbackStartedAt =
                        new DateTimeOffset(
                            file.LastWriteTimeUtc,
                            TimeSpan.Zero);

                    summaries.Add(
                        ParseSummary(
                            file.FullName,
                            prefix,
                            fallbackStartedAt));
                }
                catch
                {
                    /*
                        One damaged log must not make the entire Activity view
                        unusable. Skip only the unreadable entry.
                    */
                }
            }

            return
                summaries
                    .OrderByDescending(
                        summary =>
                            summary.StartedAt)
                    .ThenByDescending(
                        summary =>
                            summary.FileName,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(
                        maximumCount)
                    .ToArray();
        }
        catch
        {
            /*
                The Activity view is diagnostic UX. Failure to enumerate logs
                must never interfere with the assistant itself.
            */
            return
                Array.Empty<SekoTaskLogSummary>();
        }
    }

    public bool TryReadLog(
        SekoTaskLogSummary? summary,
        out string content)
    {
        content =
            string.Empty;

        if (summary is null)
        {
            return false;
        }

        try
        {
            var fullPath =
                Path.GetFullPath(
                    summary.FilePath);

            if (!IsPathInsideArchive(
                    fullPath)
                || !Path.GetExtension(
                        fullPath)
                    .Equals(
                        ".md",
                        StringComparison.OrdinalIgnoreCase)
                || !File.Exists(
                    fullPath))
            {
                return false;
            }

            content =
                File.ReadAllText(
                    fullPath,
                    Encoding.UTF8);

            return true;
        }
        catch
        {
            content =
                string.Empty;

            return false;
        }
    }

    private static SekoTaskLogSummary ParseSummary(
        string filePath,
        string prefix,
        DateTimeOffset fallbackStartedAt)
    {
        var normalized =
            prefix
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        var lines =
            normalized.Split(
                '\n');

        var status =
            GetInlineValue(
                lines,
                "Status:")
            ?? "Unknown";

        var workspace =
            GetInlineValue(
                lines,
                "Workspace:")
            ?? "Unknown workspace";

        var model =
            GetInlineValue(
                lines,
                "Model:")
            ?? "Unknown model";

        var duration =
            GetInlineValue(
                lines,
                "Duration:")
            ?? "-";

        var startedText =
            GetInlineValue(
                lines,
                "Started:");

        var startedAt =
            TryParseTimestamp(
                startedText)
            ?? fallbackStartedAt;

        var requestPreview =
            ExtractRequestPreview(
                lines);

        if (string.IsNullOrWhiteSpace(
                requestPreview))
        {
            requestPreview =
                "No request text available.";
        }

        return
            new SekoTaskLogSummary(
                Path.GetFileName(
                    filePath),
                Path.GetFullPath(
                    filePath),
                status,
                startedAt,
                workspace,
                model,
                duration,
                requestPreview);
    }

    private static string ReadPrefix(
        string filePath)
    {
        using var reader =
            new StreamReader(
                filePath,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks:
                    true);

        var buffer =
            new char[
                MaximumPrefixCharacters];

        var read =
            reader.ReadBlock(
                buffer,
                0,
                buffer.Length);

        return
            new string(
                buffer,
                0,
                read);
    }

    private static string? GetInlineValue(
        IReadOnlyList<string> lines,
        string prefix)
    {
        foreach (var line
                 in lines)
        {
            if (!line.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var value =
                line[prefix.Length..]
                    .Trim();

            return
                UnwrapInlineMarkdown(
                    value);
        }

        return null;
    }

    private static string UnwrapInlineMarkdown(
        string value)
    {
        var result =
            value.Trim();

        if (result.Length >= 4
            && result.StartsWith(
                "**",
                StringComparison.Ordinal)
            && result.EndsWith(
                "**",
                StringComparison.Ordinal))
        {
            result =
                result[2..^2]
                    .Trim();
        }

        if (result.Length >= 2
            && result.StartsWith(
                "`",
                StringComparison.Ordinal)
            && result.EndsWith(
                "`",
                StringComparison.Ordinal))
        {
            result =
                result[1..^1]
                    .Trim();
        }

        return result;
    }

    private static DateTimeOffset? TryParseTimestamp(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ExtractRequestPreview(
        IReadOnlyList<string> lines)
    {
        var requestHeaderIndex =
            -1;

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            if (lines[index].Equals(
                    "## Request",
                    StringComparison.Ordinal))
            {
                requestHeaderIndex =
                    index;

                break;
            }
        }

        if (requestHeaderIndex < 0)
        {
            return
                string.Empty;
        }

        var requestLines =
            new List<string>();

        for (var index =
                 requestHeaderIndex + 1;
             index < lines.Count;
             index++)
        {
            var line =
                lines[index];

            if (line.Equals(
                    "## Activity",
                    StringComparison.Ordinal))
            {
                break;
            }

            requestLines.Add(
                line);
        }

        var request =
            string.Join(
                " ",
                requestLines)
            .Trim();

        if (request.Equals(
                "_Empty request._",
                StringComparison.Ordinal))
        {
            return
                "Empty request.";
        }

        request =
            WhitespaceRegex.Replace(
                request,
                " ")
            .Trim();

        if (request.Length
            <= MaximumRequestPreviewLength)
        {
            return request;
        }

        return
            request[..MaximumRequestPreviewLength]
                .TrimEnd()
            + "...";
    }

    private bool IsPathInsideArchive(
        string fullPath)
    {
        var root =
            Path.GetFullPath(
                    _logDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var prefix =
            root
            + Path.DirectorySeparatorChar;

        return
            fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record SekoTaskLogSummary(
    string FileName,
    string FilePath,
    string Status,
    DateTimeOffset StartedAt,
    string WorkspaceName,
    string ModelName,
    string Duration,
    string RequestPreview)
{
    public string StartedDisplay =>
        StartedAt
            .ToLocalTime()
            .ToString(
                "MMM d, HH:mm",
                CultureInfo.CurrentCulture);

    public string WorkspaceDisplay =>
        string.IsNullOrWhiteSpace(
            Duration)
        || Duration.Equals(
            "-",
            StringComparison.Ordinal)
            ? WorkspaceName
            : WorkspaceName
              + "  ·  "
              + Duration;
}