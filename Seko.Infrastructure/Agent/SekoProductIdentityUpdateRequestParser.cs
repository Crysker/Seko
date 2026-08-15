using System.Text.RegularExpressions;

namespace Seko.Infrastructure.Agent;

public sealed record SekoProductIdentityUpdateRequest(
    string ExpectedCurrentVersion,
    string RequestedVersion,
    string RequestedDisplayName);

public static class SekoProductIdentityUpdateRequestParser
{
    private static readonly Regex ExplicitSelfIdentityUpdateRegex =
        new(
            @"^\s*update\s+yourself\s+from\s+v?(?<current>\d+\.\d+\.\d+)\s+to\s+v?(?<target>\d+\.\d+\.\d+)\s+and\s+rename\s+yourself\s+to\s+(?<name>[A-Za-z][A-Za-z0-9._-]{0,63})\s*[.!]?\s*$",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant);

    public static bool TryParse(
        string? request,
        out SekoProductIdentityUpdateRequest? updateRequest)
    {
        updateRequest =
            null;

        if (string.IsNullOrWhiteSpace(
                request))
        {
            return false;
        }

        var match =
            ExplicitSelfIdentityUpdateRegex.Match(
                request);

        if (!match.Success)
        {
            return false;
        }

        var currentVersion =
            match.Groups["current"].Value;

        var targetVersion =
            match.Groups["target"].Value;

        var requestedName =
            match.Groups["name"].Value;

        if (string.Equals(
                currentVersion,
                targetVersion,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(
                requestedName))
        {
            return false;
        }

        updateRequest =
            new SekoProductIdentityUpdateRequest(
                currentVersion,
                targetVersion,
                requestedName);

        return true;
    }
}