using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Seko.Core.Agent;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Diagnostics;

public sealed class SekoTaskLogger
{
    private const int MaximumActivityLength = 1_000;
    private const int MaximumArgumentsLength = 4_000;
    private const int MaximumResultLength = 8_000;
    private const int MaximumFinalResponseLength = 20_000;
    private const int MaximumEntries = 500;

    private static readonly Regex AssignmentSecretRegex =
        new(
            @"(?im)\b(password|passwd|pwd|api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|secret)\b\s*[:=]\s*[""']?([^\s""'\r\n]+)",
            RegexOptions.Compiled);

    private static readonly Regex JsonSecretRegex =
        new(
            @"(?im)([""'](?:password|passwd|pwd|api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|secret)[""']\s*:\s*[""'])([^""'\r\n]+)([""'])",
            RegexOptions.Compiled);

    private static readonly Regex AuthorizationRegex =
        new(
            @"(?im)\bAuthorization\s*:\s*[^\r\n]+",
            RegexOptions.Compiled);

    private static readonly Regex BearerTokenRegex =
        new(
            @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.Compiled);

    private static readonly Regex ApiTokenRegex =
        new(
            @"\bsk-[A-Za-z0-9_-]{12,}\b",
            RegexOptions.Compiled);

    private static readonly Regex GitHubTokenRegex =
        new(
            @"\b(?:github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,})\b",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase);

    private static readonly Regex PrivateKeyRegex =
        new(
            @"-----BEGIN [^-]*PRIVATE KEY-----.*?-----END [^-]*PRIVATE KEY-----",
            RegexOptions.Compiled
            | RegexOptions.Singleline
            | RegexOptions.IgnoreCase);

    private readonly string _logDirectory;

    public SekoTaskLogger()
    {
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

    public TaskLogSession? TryStart(
        Workspace workspace,
        string modelName,
        string userRequest)
    {
        try
        {
            Directory.CreateDirectory(
                _logDirectory);

            var taskId =
                Guid.NewGuid()
                    .ToString("N");

            var startedAt =
                DateTimeOffset.Now;

            var fileName =
                $"{startedAt:yyyyMMdd-HHmmss-fff}-{taskId[..8]}.md";

            var filePath =
                Path.Combine(
                    _logDirectory,
                    fileName);

            var session =
                new TaskLogSession(
                    taskId,
                    filePath,
                    startedAt,
                    workspace.Name,
                    modelName,
                    SanitizeAndTruncate(
                        userRequest,
                        MaximumFinalResponseLength));

            WriteSnapshot(
                session);

            return session;
        }
        catch
        {
            /*
                Diagnostic logging is best-effort.

                Logging must never prevent the actual Seko task from running.
            */
            return null;
        }
    }

    public void TryRecordActivity(
        TaskLogSession? session,
        AgentActivity activity)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            lock (session.SyncRoot)
            {
                AddEntry(
                    session,
                    new TaskLogEntry(
                        DateTimeOffset.Now,
                        "Activity",
                        activity.Kind.ToString(),
                        null,
                        null,
                        SanitizeAndTruncate(
                            GetActivityMessage(
                                activity),
                            MaximumActivityLength),
                        null));

                WriteSnapshotLocked(
                    session);
            }
        }
        catch
        {
            /*
                Activity logging must never affect task execution.
            */
        }
    }

    public void TryRecordDiagnostic(
        TaskLogSession? session,
        SekoDiagnosticEvent diagnosticEvent)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            lock (session.SyncRoot)
            {
                AddEntry(
                    session,
                    new TaskLogEntry(
                        diagnosticEvent.StartedAt,
                        diagnosticEvent.Kind.ToString(),
                        diagnosticEvent.Name,
                        diagnosticEvent.Duration,
                        PrepareArgumentsForLog(
                            diagnosticEvent.Name,
                            diagnosticEvent.Arguments),
                        SanitizeAndTruncate(
                            diagnosticEvent.Result
                            ?? string.Empty,
                            MaximumResultLength),
                        diagnosticEvent.Success));

                WriteSnapshotLocked(
                    session);
            }
        }
        catch
        {
            /*
                Diagnostic logging must never affect task execution.
            */
        }
    }

    public void TryFinish(
        TaskLogSession? session,
        string status,
        string? finalResponse)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            lock (session.SyncRoot)
            {
                session.Status =
                    SanitizeAndTruncate(
                        status,
                        80);

                session.FinishedAt =
                    DateTimeOffset.Now;

                session.FinalResponse =
                    SanitizeAndTruncate(
                        finalResponse
                        ?? string.Empty,
                        MaximumFinalResponseLength);

                WriteSnapshotLocked(
                    session);
            }
        }
        catch
        {
            /*
                Logging failures deliberately do not escape into task execution.
            */
        }
    }

    private static string GetActivityMessage(
        AgentActivity activity)
    {
        var activityType =
            activity.GetType();

        foreach (var propertyName
                 in new[]
                 {
                     "Message",
                     "Description",
                     "Text",
                     "Status"
                 })
        {
            var property =
                activityType.GetProperty(
                    propertyName);

            var value =
                property?.GetValue(
                    activity)
                ?.ToString();

            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return value;
            }
        }

        return
            activity.ToString()
            ?? activity.Kind.ToString();
    }

    private static void AddEntry(
        TaskLogSession session,
        TaskLogEntry entry)
    {
        if (session.Entries.Count
            >= MaximumEntries)
        {
            return;
        }

        session.Entries.Add(
            entry);
    }

    private static void WriteSnapshot(
        TaskLogSession session)
    {
        lock (session.SyncRoot)
        {
            WriteSnapshotLocked(
                session);
        }
    }

    private static void WriteSnapshotLocked(
        TaskLogSession session)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            "# Seko Task");

        builder.AppendLine();

        builder.AppendLine(
            $"Task ID: `{session.TaskId}`");

        builder.AppendLine(
            $"Status: **{EscapeInline(session.Status)}**");

        builder.AppendLine(
            $"Started: {session.StartedAt:yyyy-MM-dd HH:mm:ss.fff zzz}");

        if (session.FinishedAt.HasValue)
        {
            var duration =
                session.FinishedAt.Value
                - session.StartedAt;

            builder.AppendLine(
                $"Finished: {session.FinishedAt.Value:yyyy-MM-dd HH:mm:ss.fff zzz}");

            builder.AppendLine(
                $"Duration: {FormatDuration(duration)}");
        }
        else
        {
            builder.AppendLine(
                "Finished: -");

            builder.AppendLine(
                "Duration: -");
        }

        builder.AppendLine(
            $"Workspace: {EscapeInline(session.WorkspaceName)}");

        builder.AppendLine(
            $"Model: {EscapeInline(session.ModelName)}");

        builder.AppendLine();

        builder.AppendLine(
            "## Request");

        builder.AppendLine();

        builder.AppendLine(
            string.IsNullOrWhiteSpace(
                session.UserRequest)
                ? "_Empty request._"
                : session.UserRequest);

        builder.AppendLine();

        WriteActivitySection(
            builder,
            session);

        WriteDiagnosticSection(
            builder,
            session);

        builder.AppendLine(
            "## Final response");

        builder.AppendLine();

        if (session.FinishedAt.HasValue)
        {
            builder.AppendLine(
                string.IsNullOrWhiteSpace(
                    session.FinalResponse)
                    ? "_No final response._"
                    : session.FinalResponse);
        }
        else
        {
            builder.AppendLine(
                "_Task is still running._");
        }

        WriteSnapshotAtomically(
            session.FilePath,
            builder.ToString());
    }

    private static void WriteSnapshotAtomically(
        string filePath,
        string content)
    {
        var temporaryPath =
            filePath +
            ".tmp";

        File.WriteAllText(
            temporaryPath,
            content,
            new UTF8Encoding(
                false));

        try
        {
            if (File.Exists(
                    filePath))
            {
                File.Replace(
                    temporaryPath,
                    filePath,
                    null);
            }
            else
            {
                File.Move(
                    temporaryPath,
                    filePath);
            }
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }

    private static void WriteActivitySection(
        StringBuilder builder,
        TaskLogSession session)
    {
        builder.AppendLine(
            "## Activity");

        builder.AppendLine();

        var activities =
            session.Entries
                .Where(
                    entry =>
                        entry.Category
                            == "Activity")
                .ToList();

        if (activities.Count == 0)
        {
            builder.AppendLine(
                "_No activity events recorded yet._");

            builder.AppendLine();

            return;
        }

        foreach (var activity
                 in activities)
        {
            builder.Append(
                "- `");

            builder.Append(
                activity.Timestamp.ToString(
                    "HH:mm:ss.fff",
                    CultureInfo.InvariantCulture));

            builder.Append(
                "` **");

            builder.Append(
                EscapeInline(
                    activity.Name));

            builder.Append(
                "** ");

            builder.AppendLine(
                EscapeInline(
                    activity.Result
                    ?? string.Empty));
        }

        builder.AppendLine();
    }

    private static void WriteDiagnosticSection(
        StringBuilder builder,
        TaskLogSession session)
    {
        builder.AppendLine(
            "## Diagnostic events");

        builder.AppendLine();

        var diagnosticEntries =
            session.Entries
                .Where(
                    entry =>
                        entry.Category
                            != "Activity")
                .ToList();

        if (diagnosticEntries.Count == 0)
        {
            builder.AppendLine(
                "_No diagnostic events recorded yet._");

            builder.AppendLine();

            return;
        }

        foreach (var entry
                 in diagnosticEntries)
        {
            builder.Append(
                "### `");

            builder.Append(
                entry.Timestamp.ToString(
                    "HH:mm:ss.fff",
                    CultureInfo.InvariantCulture));

            builder.Append(
                "` ");

            builder.Append(
                EscapeInline(
                    entry.Category));

            builder.Append(
                " - `");

            builder.Append(
                EscapeInline(
                    entry.Name));

            builder.AppendLine(
                "`");

            builder.AppendLine();

            if (entry.Duration.HasValue)
            {
                builder.AppendLine(
                    $"- Duration: {FormatDuration(entry.Duration.Value)}");
            }

            if (entry.Success.HasValue)
            {
                builder.AppendLine(
                    entry.Success.Value
                        ? "- Outcome: **Success**"
                        : "- Outcome: **Failed**");
            }
            else
            {
                builder.AppendLine(
                    "- Outcome: **Informational**");
            }

            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(
                    entry.Arguments))
            {
                builder.AppendLine(
                    "**Arguments**");

                builder.AppendLine();

                AppendCodeBlock(
                    builder,
                    entry.Arguments);

                builder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(
                    entry.Result))
            {
                builder.AppendLine(
                    "**Result / error**");

                builder.AppendLine();

                AppendCodeBlock(
                    builder,
                    entry.Result);

                builder.AppendLine();
            }
        }
    }

    private static void AppendCodeBlock(
        StringBuilder builder,
        string value)
    {
        builder.AppendLine(
            "```text");

        builder.AppendLine(
            value.Replace(
                "```",
                "'''",
                StringComparison.Ordinal));

        builder.AppendLine(
            "```");
    }

    private static string PrepareArgumentsForLog(
        string toolName,
        string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        if (!toolName.Equals(
                "write_file",
                StringComparison.Ordinal)
            && !toolName.Equals(
                "replace_text",
                StringComparison.Ordinal))
        {
            return
                SanitizeAndTruncate(
                    arguments,
                    MaximumArgumentsLength);
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    arguments);

            var root =
                document.RootElement;

            var path =
                root.TryGetProperty(
                    "path",
                    out var pathElement)
                && pathElement.ValueKind == JsonValueKind.String
                    ? pathElement.GetString()
                    : null;

            if (toolName.Equals(
                    "write_file",
                    StringComparison.Ordinal))
            {
                var contentLength =
                    root.TryGetProperty(
                        "content",
                        out var contentElement)
                    && contentElement.ValueKind == JsonValueKind.String
                        ? contentElement.GetString()?.Length ?? 0
                        : 0;

                return
                    $"path={Sanitize(path ?? string.Empty)}; content_length={contentLength}";
            }

            var oldTextLength =
                root.TryGetProperty(
                    "old_text",
                    out var oldTextElement)
                && oldTextElement.ValueKind == JsonValueKind.String
                    ? oldTextElement.GetString()?.Length ?? 0
                    : 0;

            var newTextLength =
                root.TryGetProperty(
                    "new_text",
                    out var newTextElement)
                && newTextElement.ValueKind == JsonValueKind.String
                    ? newTextElement.GetString()?.Length ?? 0
                    : 0;

            return
                $"path={Sanitize(path ?? string.Empty)}; old_text_length={oldTextLength}; new_text_length={newTextLength}";
        }
        catch
        {
            return
                "[edit arguments withheld because they could contain source or sensitive content]";
        }
    }

    private static string FormatDuration(
        TimeSpan duration)
    {
        return
            duration.ToString(
                @"hh\:mm\:ss\.fff",
                CultureInfo.InvariantCulture);
    }

    private static string EscapeInline(
        string value)
    {
        return
            SanitizeAndTruncate(
                value,
                MaximumActivityLength)
            .Replace(
                "\r",
                " ")
            .Replace(
                "\n",
                " ")
            .Replace(
                "`",
                "'");
    }

    private static string SanitizeAndTruncate(
        string value,
        int maximumLength)
    {
        var sanitized =
            Sanitize(
                value);

        if (sanitized.Length
            <= maximumLength)
        {
            return sanitized;
        }

        return
            sanitized[..maximumLength]
            + Environment.NewLine
            + "[truncated]";
    }

    private static string Sanitize(
        string value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        var sanitized =
            PrivateKeyRegex.Replace(
                value,
                "[REDACTED PRIVATE KEY]");

        sanitized =
            AuthorizationRegex.Replace(
                sanitized,
                "Authorization: [REDACTED]");

        sanitized =
            BearerTokenRegex.Replace(
                sanitized,
                "Bearer [REDACTED]");

        sanitized =
            ApiTokenRegex.Replace(
                sanitized,
                "[REDACTED TOKEN]");

        sanitized =
            GitHubTokenRegex.Replace(
                sanitized,
                "[REDACTED GITHUB TOKEN]");

        sanitized =
            JsonSecretRegex.Replace(
                sanitized,
                match =>
                    match.Groups[1].Value
                    + "[REDACTED]"
                    + match.Groups[3].Value);

        sanitized =
            AssignmentSecretRegex.Replace(
                sanitized,
                match =>
                    $"{match.Groups[1].Value}=[REDACTED]");

        return sanitized;
    }

    public sealed class TaskLogSession
    {
        internal object SyncRoot
        {
            get;
        } =
            new();

        internal List<TaskLogEntry> Entries
        {
            get;
        } =
            new();

        internal string Status
        {
            get;
            set;
        } =
            "Running";

        internal DateTimeOffset? FinishedAt
        {
            get;
            set;
        }

        internal string FinalResponse
        {
            get;
            set;
        } =
            string.Empty;

        public string TaskId
        {
            get;
        }

        public string FilePath
        {
            get;
        }

        public DateTimeOffset StartedAt
        {
            get;
        }

        public string WorkspaceName
        {
            get;
        }

        public string ModelName
        {
            get;
        }

        public string UserRequest
        {
            get;
        }

        internal TaskLogSession(
            string taskId,
            string filePath,
            DateTimeOffset startedAt,
            string workspaceName,
            string modelName,
            string userRequest)
        {
            TaskId =
                taskId;

            FilePath =
                filePath;

            StartedAt =
                startedAt;

            WorkspaceName =
                workspaceName;

            ModelName =
                modelName;

            UserRequest =
                userRequest;
        }
    }

    internal sealed record TaskLogEntry(
        DateTimeOffset Timestamp,
        string Category,
        string Name,
        TimeSpan? Duration,
        string? Arguments,
        string? Result,
        bool? Success);
}

public enum SekoDiagnosticEventKind
{
    Tool,
    Build,
    Git,
    Rollback
}

public sealed record SekoDiagnosticEvent(
    DateTimeOffset StartedAt,
    SekoDiagnosticEventKind Kind,
    string Name,
    TimeSpan Duration,
    string? Arguments,
    string? Result,
    bool? Success);

public interface ISekoDiagnosticSource
{
    event Action<SekoDiagnosticEvent>? DiagnosticEvent;
}
