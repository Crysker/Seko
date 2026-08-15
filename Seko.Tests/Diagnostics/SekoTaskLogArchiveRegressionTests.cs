using Seko.Infrastructure.Diagnostics;

namespace Seko.Tests.Diagnostics;

public sealed class SekoTaskLogArchiveRegressionTests
{
    [Fact]
    public void LoadRecent_ParsesMetadataAndOrdersNewestFirst()
    {
        using var temporaryDirectory =
            new TemporaryDirectory();

        WriteLog(
            temporaryDirectory.RootPath,
            "older.md",
            """
            # Seko Task

            Task ID: `older`
            Status: **Complete**
            Started: 2026-08-15 10:00:00.000 +02:00
            Finished: 2026-08-15 10:00:01.000 +02:00
            Duration: 1.0s
            Workspace: Seko
            Model: qwen3:8b

            ## Request

            Older task.

            ## Activity

            - done
            """);

        WriteLog(
            temporaryDirectory.RootPath,
            "newer.md",
            """
            # Seko Task

            Task ID: `newer`
            Status: **Incomplete**
            Started: 2026-08-15 11:00:00.000 +02:00
            Finished: 2026-08-15 11:00:02.000 +02:00
            Duration: 2.0s
            Workspace: General
            Model: qwen3:8b

            ## Request

            Newer task with useful context.

            ## Activity

            - stopped
            """);

        var archive =
            new SekoTaskLogArchive(
                temporaryDirectory.RootPath);

        var summaries =
            archive.LoadRecent();

        Assert.Equal(
            2,
            summaries.Count);

        Assert.Equal(
            "newer.md",
            summaries[0].FileName);

        Assert.Equal(
            "Incomplete",
            summaries[0].Status);

        Assert.Equal(
            "General",
            summaries[0].WorkspaceName);

        Assert.Equal(
            "qwen3:8b",
            summaries[0].ModelName);

        Assert.Equal(
            "2.0s",
            summaries[0].Duration);

        Assert.Equal(
            "Newer task with useful context.",
            summaries[0].RequestPreview);
    }

    [Fact]
    public void LoadRecent_CollapsesAndBoundsRequestPreview()
    {
        using var temporaryDirectory =
            new TemporaryDirectory();

        var longRequest =
            string.Join(
                Environment.NewLine,
                Enumerable.Repeat(
                    "This is a deliberately long request segment.",
                    12));

        WriteLog(
            temporaryDirectory.RootPath,
            "long.md",
            $$"""
            # Seko Task

            Status: **Complete**
            Started: 2026-08-15 12:00:00.000 +02:00
            Workspace: Seko
            Model: qwen3:8b
            Duration: 1.2s

            ## Request

            {{longRequest}}

            ## Activity

            - done
            """);

        var archive =
            new SekoTaskLogArchive(
                temporaryDirectory.RootPath);

        var summary =
            Assert.Single(
                archive.LoadRecent());

        Assert.DoesNotContain(
            Environment.NewLine,
            summary.RequestPreview);

        Assert.True(
            summary.RequestPreview.Length
            <= 183);

        Assert.EndsWith(
            "...",
            summary.RequestPreview);
    }

    [Fact]
    public void LoadRecent_RespectsMaximumCount()
    {
        using var temporaryDirectory =
            new TemporaryDirectory();

        for (var index = 0;
             index < 5;
             index++)
        {
            WriteLog(
                temporaryDirectory.RootPath,
                $"task-{index}.md",
                $$"""
                # Seko Task

                Status: **Complete**
                Started: 2026-08-15 12:0{{index}}:00.000 +02:00
                Workspace: Seko
                Model: qwen3:8b
                Duration: 1.0s

                ## Request

                Task {{index}}.

                ## Activity

                - done
                """);
        }

        var archive =
            new SekoTaskLogArchive(
                temporaryDirectory.RootPath);

        var summaries =
            archive.LoadRecent(
                3);

        Assert.Equal(
            3,
            summaries.Count);
    }

    [Fact]
    public void TryReadLog_ReturnsExactStoredMarkdown()
    {
        using var temporaryDirectory =
            new TemporaryDirectory();

        const string content =
            """
            # Seko Task

            Status: **Complete**
            Started: 2026-08-15 13:00:00.000 +02:00
            Workspace: Seko
            Model: qwen3:8b
            Duration: 1.0s

            ## Request

            Read this exact log.

            ## Final response

            Done.
            """;

        WriteLog(
            temporaryDirectory.RootPath,
            "exact.md",
            content);

        var archive =
            new SekoTaskLogArchive(
                temporaryDirectory.RootPath);

        var summary =
            Assert.Single(
                archive.LoadRecent());

        var succeeded =
            archive.TryReadLog(
                summary,
                out var actual);

        Assert.True(
            succeeded);

        Assert.Equal(
            content,
            actual);
    }

    [Fact]
    public void TryReadLog_RejectsPathOutsideArchive()
    {
        using var archiveDirectory =
            new TemporaryDirectory();

        using var outsideDirectory =
            new TemporaryDirectory();

        var outsidePath =
            Path.Combine(
                outsideDirectory.RootPath,
                "outside.md");

        File.WriteAllText(
            outsidePath,
            "# outside");

        var archive =
            new SekoTaskLogArchive(
                archiveDirectory.RootPath);

        var summary =
            new SekoTaskLogSummary(
                "outside.md",
                outsidePath,
                "Complete",
                DateTimeOffset.Now,
                "Outside",
                "model",
                "1s",
                "outside");

        var succeeded =
            archive.TryReadLog(
                summary,
                out var content);

        Assert.False(
            succeeded);

        Assert.Empty(
            content);
    }

    [Fact]
    public void MissingArchive_ReturnsEmptyInsteadOfThrowing()
    {
        using var temporaryDirectory =
            new TemporaryDirectory();

        var missingPath =
            Path.Combine(
                temporaryDirectory.RootPath,
                "missing");

        var archive =
            new SekoTaskLogArchive(
                missingPath);

        Assert.Empty(
            archive.LoadRecent());
    }

    [Fact]
    public void MalformedLog_UsesSafeFallbackMetadata()
    {
        using var temporaryDirectory =
            new TemporaryDirectory();

        WriteLog(
            temporaryDirectory.RootPath,
            "malformed.md",
            """
            # Seko Task

            This log is intentionally incomplete.
            """);

        var archive =
            new SekoTaskLogArchive(
                temporaryDirectory.RootPath);

        var summary =
            Assert.Single(
                archive.LoadRecent());

        Assert.Equal(
            "Unknown",
            summary.Status);

        Assert.Equal(
            "Unknown workspace",
            summary.WorkspaceName);

        Assert.Equal(
            "Unknown model",
            summary.ModelName);

        Assert.Equal(
            "No request text available.",
            summary.RequestPreview);
    }

    private static void WriteLog(
        string directory,
        string fileName,
        string content)
    {
        File.WriteAllText(
            Path.Combine(
                directory,
                fileName),
            content);
    }

    private sealed class TemporaryDirectory :
        IDisposable
    {
        public string RootPath
        {
            get;
        }

        public TemporaryDirectory()
        {
            RootPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "Seko.ActivityHistory.Tests",
                    Guid.NewGuid()
                        .ToString("N"));

            Directory.CreateDirectory(
                RootPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(
                        RootPath))
                {
                    Directory.Delete(
                        RootPath,
                        true);
                }
            }
            catch
            {
                // Cleanup must not hide the regression result.
            }
        }
    }
}