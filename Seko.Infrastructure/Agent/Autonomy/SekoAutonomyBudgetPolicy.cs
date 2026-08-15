namespace Seko.Infrastructure.Agent;

public sealed class SekoAutonomyBudgetPolicy
{
    public static SekoAutonomyBudgetPolicy Default { get; } =
        new(
            researchRounds: 2,
            inspectionRounds: 6,
            actionRounds: 6,
            verificationRounds: 3,
            repairRounds: 4,
            synthesisRounds: 1,
            maximumRepairCycles: 2,
            maximumConsecutiveNoProgressRounds: 2,
            emergencyGlobalRoundLimit: 32);

    public static SekoAutonomyBudgetPolicy ProductIdentityUpdate { get; } =
        new(
            researchRounds: 2,
            inspectionRounds: 3,
            actionRounds: 4,
            verificationRounds: 4,
            repairRounds: 3,
            synthesisRounds: 1,
            maximumRepairCycles: 2,
            maximumConsecutiveNoProgressRounds: 2,
            emergencyGlobalRoundLimit: 20);

    public int ResearchRounds { get; }

    public int InspectionRounds { get; }

    public int ActionRounds { get; }

    public int VerificationRounds { get; }

    public int RepairRounds { get; }

    public int SynthesisRounds { get; }

    public int MaximumRepairCycles { get; }

    public int MaximumConsecutiveNoProgressRounds { get; }

    public int EmergencyGlobalRoundLimit { get; }

    public SekoAutonomyBudgetPolicy(
        int researchRounds,
        int inspectionRounds,
        int actionRounds,
        int verificationRounds,
        int repairRounds,
        int synthesisRounds,
        int maximumRepairCycles,
        int maximumConsecutiveNoProgressRounds,
        int emergencyGlobalRoundLimit)
    {
        ResearchRounds =
            RequirePositive(
                researchRounds,
                nameof(researchRounds));

        InspectionRounds =
            RequirePositive(
                inspectionRounds,
                nameof(inspectionRounds));

        ActionRounds =
            RequirePositive(
                actionRounds,
                nameof(actionRounds));

        VerificationRounds =
            RequirePositive(
                verificationRounds,
                nameof(verificationRounds));

        RepairRounds =
            RequirePositive(
                repairRounds,
                nameof(repairRounds));

        SynthesisRounds =
            RequirePositive(
                synthesisRounds,
                nameof(synthesisRounds));

        MaximumRepairCycles =
            RequirePositive(
                maximumRepairCycles,
                nameof(maximumRepairCycles));

        MaximumConsecutiveNoProgressRounds =
            RequirePositive(
                maximumConsecutiveNoProgressRounds,
                nameof(maximumConsecutiveNoProgressRounds));

        EmergencyGlobalRoundLimit =
            RequirePositive(
                emergencyGlobalRoundLimit,
                nameof(emergencyGlobalRoundLimit));
    }

    public int GetRoundBudget(
        SekoAutonomyPhase phase)
    {
        return phase switch
        {
            SekoAutonomyPhase.Research =>
                ResearchRounds,

            SekoAutonomyPhase.Inspection =>
                InspectionRounds,

            SekoAutonomyPhase.Action =>
                ActionRounds,

            SekoAutonomyPhase.Verification =>
                VerificationRounds,

            SekoAutonomyPhase.Repair =>
                RepairRounds,

            SekoAutonomyPhase.Synthesis =>
                SynthesisRounds,

            _ =>
                throw new InvalidOperationException(
                    $"Phase {phase} does not consume model-round budget.")
        };
    }

    private static int RequirePositive(
        int value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Autonomy budgets must be greater than zero.");
        }

        return value;
    }
}