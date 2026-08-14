using System.Reflection;
using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class TaskIntentRegressionTests
{
    [Theory]
    [InlineData(
        "Inspect your current agent tool architecture and build the project. Do not modify any files. Report what tools are available and whether the build succeeds.",
        true,
        false,
        true)]
    [InlineData(
        "Inspect the project without changing anything.",
        true,
        false,
        false)]
    [InlineData(
        "Review the code read-only. Do not edit anything.",
        true,
        false,
        false)]
    [InlineData(
        "Change version to v1.2.0.",
        true,
        true,
        false)]
    [InlineData(
        "Fix the Stop button.",
        true,
        true,
        false)]
    [InlineData(
        "Build the project.",
        true,
        false,
        true)]
    [InlineData(
        "Tell me a joke.",
        false,
        false,
        false)]
    public void AnalyzeTaskIntent_ClassifiesImportantRequestsCorrectly(
        string request,
        bool expectedWorkspaceTools,
        bool expectedModification,
        bool expectedExplicitBuild)
    {
        var intent =
            AnalyzeTaskIntent(
                request);

        Assert.Equal(
            expectedWorkspaceTools,
            intent.RequiresWorkspaceTools);

        Assert.Equal(
            expectedModification,
            intent.RequiresModification);

        Assert.Equal(
            expectedExplicitBuild,
            intent.ExplicitBuildRequested);
    }

    [Theory]
    [InlineData("Do not modify the project.")]
    [InlineData("Don't change the code.")]
    [InlineData("Inspect only; do not write files.")]
    [InlineData("Review the repository without editing anything.")]
    [InlineData("Check the UI without making changes.")]
    public void ExplicitReadOnlyLanguage_OverridesMutationKeywords(
        string request)
    {
        var intent =
            AnalyzeTaskIntent(
                request);

        Assert.True(
            intent.RequiresWorkspaceTools);

        Assert.False(
            intent.RequiresModification);
    }

    private static TaskIntentSnapshot AnalyzeTaskIntent(
        string request)
    {
        var method =
            typeof(OllamaAgent).GetMethod(
                "AnalyzeTaskIntent",
                BindingFlags.NonPublic
                | BindingFlags.Static);

        Assert.NotNull(
            method);

        var result =
            method!.Invoke(
                null,
                new object[]
                {
                    request
                });

        Assert.NotNull(
            result);

        var resultType =
            result!.GetType();

        return
            new TaskIntentSnapshot(
                ReadBooleanProperty(
                    result,
                    resultType,
                    "RequiresWorkspaceTools"),
                ReadBooleanProperty(
                    result,
                    resultType,
                    "RequiresModification"),
                ReadBooleanProperty(
                    result,
                    resultType,
                    "ExplicitBuildRequested"));
    }

    private static bool ReadBooleanProperty(
        object instance,
        Type instanceType,
        string propertyName)
    {
        var property =
            instanceType.GetProperty(
                propertyName,
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic);

        Assert.NotNull(
            property);

        var value =
            property!.GetValue(
                instance);

        Assert.IsType<bool>(
            value);

        return
            (bool)value!;
    }

    private sealed record TaskIntentSnapshot(
        bool RequiresWorkspaceTools,
        bool RequiresModification,
        bool ExplicitBuildRequested);
}
