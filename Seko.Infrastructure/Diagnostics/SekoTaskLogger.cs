using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Diagnostics;

public sealed class SekoTaskLogger
{
    private static readonly Regex AssignmentSecretRegex =
        new(
            @"(?im)\b(password|passwd|pwd|api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|secret)\b\s*[:=]\s*[""']?([^\s""'\r\n]+)",
            RegexOptions.Compiled);

    private static readonly Regex BearerTokenRegex =
        new(
            @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.Compiled);

    private static readonly Regex ApiTokenRegex =
        new(
            @"\bsk-[A-Za-z0-9_-]{12,}\b",
            RegexOptions.Compiled);

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
                    Sanitize(
                        userRequest));

            Write(
                session,
                "Running",
                null,
                null);

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
            var finishedAt =
                DateTimeOffset.Now;

            Write(
                session,
                status,
                finishedAt,
                Sanitize(
                    finalResponse
                    ?? string.Empty));
        }
        catch
        {
            /*
                Logging failures deliberately do not escape into task execution.
            */
        }
    }

    private static void Write(
        TaskLogSession session,
        string status,
        DateTimeOffset? finishedAt,
        string? finalResponse)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            "# Seko Task");

        builder.AppendLine();

        builder.AppendLine(
            $"Task ID: `{session.TaskId}`");

        builder.AppendLine(
            $"Status: **{EscapeInline(status)}**");

        builder.AppendLine(
            $"Started: {session.StartedAt:yyyy-MM-dd HH:mm:ss.fff zzz}");

        if (finishedAt.HasValue)
        {
            var duration =
                finishedAt.Value
                - session.StartedAt;

            builder.AppendLine(
                $"Finished: {finishedAt.Value:yyyy-MM-dd HH:mm:ss.fff zzz}");

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

        builder.AppendLine(
            "## Final response");

        builder.AppendLine();

        if (finishedAt.HasValue)
        {
            builder.AppendLine(
                string.IsNullOrWhiteSpace(
                    finalResponse)
                    ? "_No final response._"
                    : finalResponse);
        }
        else
        {
            builder.AppendLine(
                "_Task is still running._");
        }

        File.WriteAllText(
            session.FilePath,
            builder.ToString(),
            new UTF8Encoding(
                false));
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
            Sanitize(
                value)
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
            BearerTokenRegex.Replace(
                sanitized,
                "Bearer [REDACTED]");

        sanitized =
            ApiTokenRegex.Replace(
                sanitized,
                "[REDACTED TOKEN]");

        sanitized =
            AssignmentSecretRegex.Replace(
                sanitized,
                match =>
                    $"{match.Groups[1].Value}=[REDACTED]");

        return sanitized;
    }

    public sealed record TaskLogSession(
        string TaskId,
        string FilePath,
        DateTimeOffset StartedAt,
        string WorkspaceName,
        string ModelName,
        string UserRequest);
}