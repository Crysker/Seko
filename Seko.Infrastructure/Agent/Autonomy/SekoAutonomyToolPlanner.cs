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

    private static readonly IReadOnlyCollection<string> ProductIdentityInspectionTools =
        Array.AsReadOnly(
            new[]
            {
                "inspect_product_identity"
            });

    private static readonly IReadOnlyCollection<string> ProductIdentityActionTools =
        Array.AsReadOnly(
            new[]
            {
                "update_product_identity"
            });

    private static readonly IReadOnlyCollection<string> ProductIdentityVerificationTools =
        Array.AsReadOnly(
            new[]
            {
                "build_project",
                "test_project",
                "verify_product_identity"
            });

    private static readonly IReadOnlyCollection<string> ProductIdentityRepairTools =
        Array.AsReadOnly(
            new[]
            {
                "inspect_product_identity",
                "read_file",
                "replace_text",
                "git_diff"
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

        if (state.ProductIdentityUpdateRequired)
        {
            return CreateProductIdentityPlan(
                state.Phase);
        }

        return Create(
            state.Phase);
    }

    private static SekoAutonomyToolPlan CreateProductIdentityPlan(
        SekoAutonomyPhase phase)
    {
        return phase switch
        {
            SekoAutonomyPhase.Inspection =>
                new SekoAutonomyToolPlan(
                    phase,
                    ProductIdentityInspectionTools,
                    "Product identity inspection is deliberately narrow: inspect only the canonical identity source and its consumers."),

            SekoAutonomyPhase.Action =>
                new SekoAutonomyToolPlan(
                    phase,
                    ProductIdentityActionTools,
                    "Product identity action is host-owned: call update_product_identity once. The host applies the accepted inspection target without model-generated old_text."),

            SekoAutonomyPhase.Verification =>
                new SekoAutonomyToolPlan(
                    phase,
                    ProductIdentityVerificationTools,
                    "Product identity verification requires build_project, test_project and verify_product_identity for the same modification generation."),

            SekoAutonomyPhase.Repair =>
                new SekoAutonomyToolPlan(
                    phase,
                    ProductIdentityRepairTools,
                    "Repair only the canonical identity edit using concrete verification failure evidence."),

            SekoAutonomyPhase.Synthesis =>
                None(
                    phase,
                    "Synthesis produces the final response after all product identity gates pass."),

            _ =>
                Create(
                    phase)
        };
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