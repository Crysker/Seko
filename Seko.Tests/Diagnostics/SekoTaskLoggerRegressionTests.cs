using System.Reflection;
using System.Text.Json;
using Seko.Infrastructure.Diagnostics;

namespace Seko.Tests.Diagnostics;

public sealed class SekoTaskLoggerRegressionTests
{
    [Theory]
    [InlineData(
        "password=super-secret-value",
        "super-secret-value")]
    [InlineData(
        "Authorization: Bearer abcdefghijklmnopqrstuvwxyz",
        "abcdefghijklmnopqrstuvwxyz")]
    [InlineData(
        "token=github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ123456",
        "github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ123456")]
    [InlineData(
        "token=ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ123456",
        "ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ123456")]
    [InlineData(
        "api_key=sk-abcdefghijklmnopqrstuvwxyz123456",
        "sk-abcdefghijklmnopqrstuvwxyz123456")]
    public void Sanitize_RemovesKnownSecretFormats(
        string input,
        string secret)
    {
        var sanitized =
            InvokePrivateStringMethod(
                "Sanitize",
                new object[]
                {
                    input
                });

        Assert.DoesNotContain(
            secret,
            sanitized);

        Assert.Contains(
            "REDACTED",
            sanitized,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteFileArguments_DoNotPersistSourceContent()
    {
        const string secret =
            "github_pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ123456";

        var arguments =
            JsonSerializer.Serialize(
                new
                {
                    path = "Seko.Infrastructure/Example.cs",
                    content =
                        "public static class Example { "
                        + "public const string Token = \""
                        + secret
                        + "\"; }"
                });

        var prepared =
            InvokePrivateStringMethod(
                "PrepareArgumentsForLog",
                new object[]
                {
                    "write_file",
                    arguments
                });

        Assert.Contains(
            "path=Seko.Infrastructure/Example.cs",
            prepared);

        Assert.Contains(
            "content_length=",
            prepared);

        Assert.DoesNotContain(
            "public static class Example",
            prepared);

        Assert.DoesNotContain(
            secret,
            prepared);
    }

    [Fact]
    public void ReplaceTextArguments_DoNotPersistOldOrNewSource()
    {
        var arguments =
            JsonSerializer.Serialize(
                new
                {
                    path = "Seko.Infrastructure/Example.cs",
                    old_text = "private const string OldValue = \"secret-old\";",
                    new_text = "private const string NewValue = \"secret-new\";"
                });

        var prepared =
            InvokePrivateStringMethod(
                "PrepareArgumentsForLog",
                new object[]
                {
                    "replace_text",
                    arguments
                });

        Assert.Contains(
            "path=Seko.Infrastructure/Example.cs",
            prepared);

        Assert.Contains(
            "old_text_length=",
            prepared);

        Assert.Contains(
            "new_text_length=",
            prepared);

        Assert.DoesNotContain(
            "secret-old",
            prepared);

        Assert.DoesNotContain(
            "secret-new",
            prepared);
    }

    private static string InvokePrivateStringMethod(
        string methodName,
        object[] arguments)
    {
        var method =
            typeof(SekoTaskLogger).GetMethod(
                methodName,
                BindingFlags.NonPublic
                | BindingFlags.Static);

        Assert.NotNull(
            method);

        var result =
            method!.Invoke(
                null,
                arguments);

        Assert.IsType<string>(
            result);

        return
            (string)result!;
    }
}
