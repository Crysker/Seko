namespace Seko.Infrastructure.Agent;

public static class SekoAutonomyToolPlanner
{
    private static readonly IReadOnlyCollection<string> ResearchTools =
        Array.AsReadOnly(
            new[]
            {
                "web_research",
                "web_fetch"
            });

    private static readonly IReadOnlyCollection<string> InspectionTools =
        Array.AsReadOnly(
            new[]
            {
                "search_workspace",
                "find_files",
                "find_text",
                "list_files",
                "read_file",
                "read_task_log"
            });

    private static readonly IReadOnlyCollection<string> ActionTools =
        Array.AsReadOnly(
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
            });

    private static readonly IReadOnlyCollection<string> VerificationTools =
        Array.AsReadOnly(
            new[]
            {
                "search_workspace",
                "find_files",
                "find_text",
                "list_files",
                "read_file",
                "verify_file",
                "build_project",
                "git_status",
                "git_diff"
            });

    private static readonly IReadOnlyCollection<string> RepairTools =
        Array.AsReadOnly(
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
            });

    private static readonly IReadOnlyCollection<string> NoTools =
        Array.Empty<string>();

    public static SekoAutonomyToolPlan Create(
        SekoAutonomyState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        if ((state.Phase
                 is SekoAutonomyPhase.Action
                     or SekoAutonomyPhase.Repair)
            && !state.WorkspaceModificationAllowed)
        {
            return None(
                state.Phase,
                "The original task did not grant workspace modification permission; write-capable phases fail closed.");
        }

        return Create(
            state.Phase);
    }

    public static SekoAutonomyToolPlan Create(
        SekoAutonomyPhase phase)
    {
        return phase switch
        {
            SekoAutonomyPhase.Planning =>
                None(
                    phase,
                    "Planning is a controller-only transition phase; no tools are exposed."),

            SekoAutonomyPhase.Research =>
                new SekoAutonomyToolPlan(
                    phase,
                    ResearchTools,
                    "Research may use only web evidence-gathering tools."),

            SekoAutonomyPhase.Inspection =>
                new SekoAutonomyToolPlan(
                    phase,
                    InspectionTools,
                    "Inspection is read-only and may gather workspace or diagnostic evidence."),

            SekoAutonomyPhase.Action =>
                new SekoAutonomyToolPlan(
                    phase,
                    ActionTools,
                    "Action may inspect, modify, build, and inspect the resulting Git diff."),

            SekoAutonomyPhase.Verification =>
                new SekoAutonomyToolPlan(
                    phase,
                    VerificationTools,
                    "Verification may inspect, run deterministic non-build artifact verification, and build, but cannot modify workspace files."),

            SekoAutonomyPhase.Repair =>
                new SekoAutonomyToolPlan(
                    phase,
                    RepairTools,
                    "Repair is intentionally narrower than normal action and is limited to targeted diagnosis, edits, build, and diff inspection."),

            SekoAutonomyPhase.Synthesis =>
                None(
                    phase,
                    "Synthesis produces the final response from collected evidence and should not call tools."),

            SekoAutonomyPhase.Complete =>
                None(
                    phase,
                    "The autonomy task is complete; no further tools are permitted."),

            SekoAutonomyPhase.Incomplete =>
                None(
                    phase,
                    "The autonomy task terminated incomplete; no further tools are permitted."),

            _ =>
                None(
                    phase,
                    $"No tool policy is defined for autonomy phase '{phase}'.")
        };
    }

    private static SekoAutonomyToolPlan None(
        SekoAutonomyPhase phase,
        string reason)
    {
        return new SekoAutonomyToolPlan(
            phase,
            NoTools,
            reason);
    }
}