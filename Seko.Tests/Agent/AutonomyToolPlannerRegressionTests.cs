using Seko.Infrastructure.Agent;

namespace Seko.Tests.Agent;

public sealed class AutonomyToolPlannerRegressionTests
{
    [Fact]
    public void Planning_AllowsNoTools()
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Planning);

        Assert.Empty(
            plan.ToolNames);

        Assert.False(
            plan.Allows(
                "read_file"));
    }

    [Fact]
    public void Research_ExposesOnlyWebEvidenceTools()
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Research);

        Assert.Equal(
            new[]
            {
                "web_research",
                "web_fetch"
            },
            plan.ToolNames);

        Assert.True(
            plan.Allows(
                "web_research"));

        Assert.True(
            plan.Allows(
                "web_fetch"));

        Assert.False(
            plan.Allows(
                "read_file"));

        Assert.False(
            plan.Allows(
                "write_file"));
    }

    [Fact]
    public void Inspection_IsReadOnly()
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Inspection);

        Assert.Equal(
            new[]
            {
                "search_workspace",
                "find_files",
                "find_text",
                "list_files",
                "read_file",
                "read_task_log"
            },
            plan.ToolNames);

        Assert.True(
            plan.Allows(
                "read_file"));

        Assert.True(
            plan.Allows(
                "read_task_log"));

        Assert.False(
            plan.Allows(
                "write_file"));

        Assert.False(
            plan.Allows(
                "replace_text"));

        Assert.False(
            plan.Allows(
                "build_project"));
    }

    [Fact]
    public void Action_AllowsBoundedWorkspaceModification()
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Action);

        Assert.Equal(
            new[]
            {
                "search_workspace",
                "find_files",
                "find_text",
                "list_files",
                "read_file",
                "write_file",
                "replace_text",
                "build_project",
                "git_status",
                "git_diff"
            },
            plan.ToolNames);

        Assert.True(
            plan.Allows(
                "write_file"));

        Assert.True(
            plan.Allows(
                "replace_text"));

        Assert.True(
            plan.Allows(
                "build_project"));

        Assert.True(
            plan.Allows(
                "git_diff"));

        Assert.False(
            plan.Allows(
                "web_research"));
    }

    [Fact]
    public void Verification_CannotModifyWorkspace()
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Verification);

        Assert.Equal(
            new[]
            {
                "search_workspace",
                "find_files",
                "find_text",
                "list_files",
                "read_file",
                "build_project",
                "git_status",
                "git_diff"
            },
            plan.ToolNames);

        Assert.True(
            plan.Allows(
                "build_project"));

        Assert.True(
            plan.Allows(
                "git_diff"));

        Assert.False(
            plan.Allows(
                "write_file"));

        Assert.False(
            plan.Allows(
                "replace_text"));
    }

    [Fact]
    public void Repair_IsNarrowerThanNormalAction()
    {
        var action =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Action);

        var repair =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Repair);

        Assert.Equal(
            new[]
            {
                "find_files",
                "find_text",
                "read_file",
                "write_file",
                "replace_text",
                "build_project",
                "git_status",
                "git_diff"
            },
            repair.ToolNames);

        Assert.True(
            repair.Allows(
                "replace_text"));

        Assert.True(
            repair.Allows(
                "build_project"));

        Assert.False(
            repair.Allows(
                "search_workspace"));

        Assert.False(
            repair.Allows(
                "list_files"));

        Assert.True(
            repair.ToolNames.Count
            < action.ToolNames.Count);
    }

    [Fact]
    public void Synthesis_AllowsNoTools()
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Synthesis);

        Assert.Empty(
            plan.ToolNames);
    }

    [Theory]
    [InlineData(SekoAutonomyPhase.Complete)]
    [InlineData(SekoAutonomyPhase.Incomplete)]
    public void TerminalPhases_AllowNoTools(
        SekoAutonomyPhase phase)
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                phase);

        Assert.Empty(
            plan.ToolNames);

        Assert.False(
            plan.Allows(
                "read_file"));

        Assert.False(
            plan.Allows(
                "write_file"));

        Assert.False(
            plan.Allows(
                "web_research"));
    }

    [Fact]
    public void UnknownPhase_FailsClosed()
    {
        var unknown =
            (SekoAutonomyPhase)999;

        var plan =
            SekoAutonomyToolPlanner.Create(
                unknown);

        Assert.Empty(
            plan.ToolNames);

        Assert.False(
            plan.Allows(
                "read_file"));

        Assert.Contains(
            "No tool policy",
            plan.Reason);
    }

    [Fact]
    public void StateOverload_UsesCurrentPhaseWithoutMutatingState()
    {
        var state =
            new SekoAutonomyState
            {
                Phase =
                    SekoAutonomyPhase.Repair,

                TotalModelRounds =
                    7,

                PhaseModelRounds =
                    2,

                ConsecutiveNoProgressRounds =
                    1,

                RepairCycles =
                    1,

                ModificationGeneration =
                    2,

                LastVerificationFailureSignature =
                    "CS1002"
            };

        var before =
            state;

        var plan =
            SekoAutonomyToolPlanner.Create(
                state);

        Assert.Equal(
            SekoAutonomyPhase.Repair,
            plan.Phase);

        Assert.Same(
            before,
            state);

        Assert.Equal(
            7,
            state.TotalModelRounds);

        Assert.Equal(
            2,
            state.PhaseModelRounds);

        Assert.Equal(
            1,
            state.ConsecutiveNoProgressRounds);

        Assert.Equal(
            "CS1002",
            state.LastVerificationFailureSignature);
    }

    [Fact]
    public void Allows_IsOrdinalAndRejectsBlankToolNames()
    {
        var plan =
            SekoAutonomyToolPlanner.Create(
                SekoAutonomyPhase.Action);

        Assert.True(
            plan.Allows(
                "read_file"));

        Assert.False(
            plan.Allows(
                "READ_FILE"));

        Assert.False(
            plan.Allows(
                string.Empty));

        Assert.False(
            plan.Allows(
                "   "));
    }
}