namespace PuckDrop.Web.Layout;

public enum NavSection
{
    None,
    Home,
    Leaderboard,
    History,
    Admin
}

public static class NavSections
{
    /// <summary>
    /// Which top-level nav section a base-relative path belongs to, so the nav can mark it as the
    /// current page on child pages too - a poll counts as Home, results as History.
    /// </summary>
    public static NavSection FromRelativePath(string relativePath)
    {
        var end = relativePath.IndexOfAny(['?', '#']);
        var path = (end >= 0 ? relativePath[..end] : relativePath).Trim('/');

        if (path.Length == 0 || IsUnder(path, "poll"))
            return NavSection.Home;
        if (IsUnder(path, "leaderboard"))
            return NavSection.Leaderboard;
        if (IsUnder(path, "history") || IsUnder(path, "results"))
            return NavSection.History;
        if (IsUnder(path, "admin"))
            return NavSection.Admin;

        return NavSection.None;
    }

    private static bool IsUnder(string path, string segment) =>
        path.Equals(segment, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase);
}
