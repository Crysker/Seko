using System.Text.RegularExpressions;
using Seko.Infrastructure.Agent.Permissions;

namespace Seko.Infrastructure.Agent.Extensions;

public sealed class SekoExtensionValidator
{
    private static readonly Regex IdRegex =
        new(
            "^[a-z0-9][a-z0-9._-]{0,63}$",
            RegexOptions.Compiled
            | RegexOptions.CultureInvariant);

    private static readonly Regex VersionRegex =
        new(
            @"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$",
            RegexOptions.Compiled
            | RegexOptions.CultureInvariant);

    public IReadOnlyCollection<string> Validate(
        SekoExtensionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(
            manifest);

        var errors =
            new List<string>();

        if (manifest.SchemaVersion != 1)
        {
            errors.Add(
                $"Unsupported extension schema version '{manifest.SchemaVersion}'.");
        }

        if (!IdRegex.IsMatch(
                manifest.Id?.Trim()
                ?? string.Empty))
        {
            errors.Add(
                "Extension id must use lowercase letters, numbers, dots, underscores or hyphens and be at most 64 characters.");
        }

        if (string.IsNullOrWhiteSpace(
                manifest.Name))
        {
            errors.Add(
                "Extension name cannot be empty.");
        }
        else if (manifest.Name.Trim().Length > 120)
        {
            errors.Add(
                "Extension name is too long.");
        }

        if ((manifest.Description?.Length
             ?? 0) > 2_000)
        {
            errors.Add(
                "Extension description is too long.");
        }

        if (!VersionRegex.IsMatch(
                manifest.Version?.Trim()
                ?? string.Empty))
        {
            errors.Add(
                "Extension version must use semantic version form such as 1.0.0.");
        }

        if (!string.Equals(
                manifest.Runtime?.Trim(),
                "declarative-v1",
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "Adaptive Platform v1 only accepts runtime 'declarative-v1'. Arbitrary in-process extension code is not allowed.");
        }

        if ((manifest.Abilities?.Count
             ?? 0) > 64)
        {
            errors.Add(
                "Extension declares too many abilities.");
        }

        if ((manifest.Permissions?.Count
             ?? 0) > 64)
        {
            errors.Add(
                "Extension declares too many permissions.");
        }

        ValidateValues(
            manifest.Abilities,
            "ability",
            errors);

        ValidateValues(
            manifest.Permissions,
            "permission",
            errors);

        foreach (var permission
                 in manifest.Permissions
                    ?? new List<string>())
        {
            if (SekoPermissionPolicy.IsProtectedPermission(
                    permission))
            {
                errors.Add(
                    $"Extension cannot request protected permission '{permission}'.");
            }
        }

        if (manifest.Skills is null)
        {
            errors.Add(
                "Extension skill collection cannot be null.");

            return
                errors.AsReadOnly();
        }

        if (manifest.Skills.Count > 32)
        {
            errors.Add(
                "Extension declares too many skills.");
        }

        var skillIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var skill
                 in manifest.Skills)
        {
            if (skill is null)
            {
                errors.Add(
                    "Extension skill cannot be null.");

                continue;
            }

            var normalizedSkillId =
                skill.Id?.Trim()
                ?? string.Empty;

            if (!IdRegex.IsMatch(
                    normalizedSkillId))
            {
                errors.Add(
                    "Extension skill id is invalid.");
            }
            else if (!skillIds.Add(
                         normalizedSkillId))
            {
                errors.Add(
                    $"Extension contains duplicate skill id '{skill.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(
                    skill.Name))
            {
                errors.Add(
                    $"Extension skill '{skill.Id}' has no name.");
            }
            else if (skill.Name.Trim().Length > 120)
            {
                errors.Add(
                    $"Extension skill '{skill.Id}' name is too long.");
            }

            if ((skill.Instructions?.Length
                 ?? 0) > 4_000)
            {
                errors.Add(
                    $"Extension skill '{skill.Id}' instructions are too long.");
            }

            if ((skill.TriggerTerms?.Count
                 ?? 0) > 32
                || (skill.RequiredAbilities?.Count
                    ?? 0) > 32
                || (skill.PreferredAbilities?.Count
                    ?? 0) > 32)
            {
                errors.Add(
                    $"Extension skill '{skill.Id}' contains too many declarations.");
            }

            ValidateValues(
                skill.TriggerTerms,
                $"skill '{skill.Id}' trigger term",
                errors);

            ValidateValues(
                skill.RequiredAbilities,
                $"skill '{skill.Id}' required ability",
                errors);

            ValidateValues(
                skill.PreferredAbilities,
                $"skill '{skill.Id}' preferred ability",
                errors);
        }

        return
            errors.AsReadOnly();
    }

    private static void ValidateValues(
        IEnumerable<string>? values,
        string valueKind,
        ICollection<string> errors)
    {
        if (values is null)
        {
            errors.Add(
                $"Extension {valueKind} collection cannot be null.");

            return;
        }

        var materialized =
            values
                .Select(
                    value =>
                        value?.Trim()
                        ?? string.Empty)
                .ToList();

        if (materialized.Any(
                string.IsNullOrWhiteSpace))
        {
            errors.Add(
                $"Extension {valueKind} values cannot be empty.");
        }

        if (materialized.Any(
                value =>
                    value.Length > 160))
        {
            errors.Add(
                $"Extension {valueKind} values are too long.");
        }

        if (materialized.Count
            != materialized
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count())
        {
            errors.Add(
                $"Extension {valueKind} values must be unique.");
        }
    }
}
