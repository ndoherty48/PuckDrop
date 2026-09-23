using System.Text.RegularExpressions;

namespace PuckDrop.Application.Services;

/// <summary>
/// Parses the DESCRIPTION text of a fixture calendar event. The per-team ICS feeds this app
/// imports from carry SUMMARY relative to that team ("vs Opponent" / "@ Opponent") but DESCRIPTION
/// has the full matchup: "{Competition}: {Home} vs {Away} @ {Venue} on {DD/MM/YYYY HH:mm}". Pure
/// text in, no calendar library dependency - only Infrastructure knows about Ical.Net, matching
/// how only Infrastructure knows about DynamoDB.
/// </summary>
public static partial class FixtureDescriptionParser
{
    [GeneratedRegex(@"^(?<competition>.+?):\s*(?<home>.+?)\s+vs\s+(?<away>.+?)\s+@\s+(?<venue>.+?)\s+on\s+\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}$")]
    private static partial Regex DescriptionPattern();

    /// <summary>
    /// Returns the parsed fields, or null when the description doesn't match the expected shape -
    /// the caller falls back to the event's raw SUMMARY/CATEGORIES instead of guessing.
    /// </summary>
    public static ParsedFixtureDescription? Parse(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var match = DescriptionPattern().Match(description.Trim());
        if (!match.Success)
            return null;

        return new ParsedFixtureDescription(
            match.Groups["competition"].Value.Trim(),
            match.Groups["home"].Value.Trim(),
            match.Groups["away"].Value.Trim(),
            match.Groups["venue"].Value.Trim());
    }
}

public record ParsedFixtureDescription(string Competition, string Home, string Away, string Venue);
