namespace PuckDrop.Web.Display;

/// <summary>
/// Small text-formatting helpers shared by the layout and pages.
/// </summary>
public static class DisplayText
{
    private static readonly char[] NameSeparators = [' ', '.', '_', '-'];

    /// <summary>
    /// Up to two initials for the account avatar - "Sam Rafferty" -> "SR", "friend" -> "F".
    /// Cognito users can fall back to an email as their name (see PuckDropClaimsPrincipalFactory),
    /// so only the part before the @ is used.
    /// </summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var at = name.IndexOf('@');
        var localPart = at > 0 ? name[..at] : name;
        var parts = localPart.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
            return "?";

        var first = char.ToUpperInvariant(parts[0][0]);
        return parts.Length == 1 ? first.ToString() : $"{first}{char.ToUpperInvariant(parts[^1][0])}";
    }

    /// <summary>
    /// "In 5 days" / "In 3 hours" / "In 20 minutes" until a poll deadline, or "Closed" once it's
    /// passed. Compares the same way Poll.razor decides whether voting is closed.
    /// </summary>
    public static string TimeUntilDeadline(DateTime deadline, DateTime now)
    {
        var remaining = deadline - now;

        if (remaining <= TimeSpan.Zero)
            return "Closed";
        if (remaining.TotalHours < 1)
            return InUnits(Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)), "minute");
        if (remaining.TotalDays < 1)
            return InUnits((int)remaining.TotalHours, "hour");

        return InUnits((int)remaining.TotalDays, "day");
    }

    private static string InUnits(int count, string unit) => $"In {count} {unit}{(count == 1 ? "" : "s")}";

    /// <summary>
    /// A UTC deadline, formatted in the viewer's own timezone - "Sat 10 Oct, 18:00" for someone in
    /// UTC, "Sat 10 Oct, 19:00" for someone in BST, same instant either way.
    /// </summary>
    public static string LocalDeadline(DateTime utcDeadline, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(utcDeadline, timeZone).ToString("ddd d MMM, HH:mm");

    /// <summary>
    /// Names as a sentence fragment: "Mark", "Dee and Mark", "Ciaran, Dee and Mark", and past
    /// <paramref name="max"/> names, "Ciaran, Dee, Mark and 2 others".
    /// </summary>
    public static string NameList(IReadOnlyList<string> names, int max = 3)
    {
        if (names.Count <= max)
            return names.Count <= 1
                ? string.Concat(names)
                : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

        var others = names.Count - max;
        return $"{string.Join(", ", names.Take(max))} and {others} {(others == 1 ? "other" : "others")}";
    }
}
