using System.Text.RegularExpressions;
using Seko.Infrastructure.Agent.Capabilities;
using Seko.Infrastructure.Agent.Projects;

namespace Seko.Infrastructure.Agent.Skills;

public sealed class SekoSkillResolver
{
    public IReadOnlyCollection<SkillResolution> Resolve(
        string task,
        SekoProjectProfile project,
        SekoSkillRegistry registry,
        SekoCapabilityRegistry capabilityRegistry,
        int maximumSkills = 3)
    {
        ArgumentNullException.ThrowIfNull(
            project);

        ArgumentNullException.ThrowIfNull(
            registry);

        ArgumentNullException.ThrowIfNull(
            capabilityRegistry);

        if (maximumSkills <= 0)
        {
            return
                Array.Empty<SkillResolution>();
        }

        var normalizedTask =
            task?.Trim()
            ?? string.Empty;

        var enabledSkills =
            project.EnabledSkills
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var results =
            new List<SkillResolution>();

        foreach (var skill
                 in registry.Skills)
        {
            var descriptor =
                skill.Descriptor;

            var score =
                descriptor.Priority;

            var matchedTerms =
                descriptor.TriggerTerms.Count(
                    term =>
                        ContainsTerm(
                            normalizedTask,
                            term));

            score +=
                matchedTerms * 20;

            if (enabledSkills.Contains(
                    descriptor.Id))
            {
                score +=
                    100;
            }

            if (matchedTerms == 0
                && !enabledSkills.Contains(
                    descriptor.Id))
            {
                continue;
            }

            var missingRequired =
                descriptor.RequiredAbilities
                    .Where(
                        ability =>
                            !capabilityRegistry.Supports(
                                ability))
                    .ToList()
                    .AsReadOnly();

            var missingPreferred =
                descriptor.PreferredAbilities
                    .Where(
                        ability =>
                            !capabilityRegistry.Supports(
                                ability))
                    .ToList()
                    .AsReadOnly();

            score +=
                descriptor.RequiredAbilities.Count
                - missingRequired.Count;

            results.Add(
                new SkillResolution(
                    skill,
                    score,
                    missingRequired,
                    missingPreferred));
        }

        return
            results
                .OrderByDescending(
                    result =>
                        result.Score)
                .ThenBy(
                    result =>
                        result.Skill.Descriptor.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Take(
                    maximumSkills)
                .ToList()
                .AsReadOnly();
    }

    private static bool ContainsTerm(
        string text,
        string term)
    {
        if (string.IsNullOrWhiteSpace(
                text)
            || string.IsNullOrWhiteSpace(
                term))
        {
            return false;
        }

        return
            Regex.IsMatch(
                text,
                $@"(?<![A-Za-z0-9]){Regex.Escape(term.Trim())}(?![A-Za-z0-9])",
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant);
    }
}
