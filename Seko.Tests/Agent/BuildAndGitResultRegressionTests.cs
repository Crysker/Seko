using System.Reflection;
using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class BuildAndGitResultRegressionTests
{
    [Theory]
    [InlineData("BUILD TARGET: Seko.sln\nBUILD EXIT CODE: 0\n\nBuild succeeded.", true)]
    [InlineData("BUILD TARGET: Seko.sln\nBUILD EXIT CODE: 1\n\nBuild failed.", false)]
    [InlineData("ERROR: No .sln or .csproj file was found in this workspace.", false)]
    [InlineData("", false)]
    public void BuildResultDetection_UsesExplicitExitCode(
        string result,
        bool expected)
    {
        var actual =
            InvokePrivateBooleanMethod(
                "IsSuccessfulBuildResult",
                result);

        Assert.Equal(
            expected,
            actual);
    }

    [Theory]
    [InlineData("Git: staging failed.\npermission denied", true)]
    [InlineData("Git: changes were staged, but the commit failed.\nidentity unknown", true)]
    [InlineData("Git: changes were not committed because a successful build was not verified.", true)]
    [InlineData("Git: committed locally as abc12345 - Seko: test", false)]
    [InlineData("Git: there were no effective changes to commit.", false)]
    public void BlockingGitFailureDetection_DoesNotTreatCommitFailuresAsSuccess(
        string result,
        bool expected)
    {
        var actual =
            InvokePrivateBooleanMethod(
                "IsBlockingGitFinalizationFailure",
                result);

        Assert.Equal(
            expected,
            actual);
    }

    private static bool InvokePrivateBooleanMethod(
        string methodName,
        string argument)
    {
        var method =
            typeof(OllamaAgent).GetMethod(
                methodName,
                BindingFlags.NonPublic
                | BindingFlags.Static);

        Assert.NotNull(
            method);

        var result =
            method!.Invoke(
                null,
                new object[]
                {
                    argument
                });

        Assert.IsType<bool>(
            result);

        return
            (bool)result!;
    }
}
