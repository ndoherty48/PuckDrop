namespace PuckDrop.E2ETests;

/// <summary>
/// Seed data from the imported Keycloak realm (Keycloak/PuckDrop-realm.json) - two fixed users,
/// persistent across the whole test run, covering both roles this app cares about.
/// </summary>
internal static class TestData
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin";
    public const string AdminDisplayName = "Admin User";

    public const string FriendUsername = "friend";
    public const string FriendPassword = "friend";
    public const string FriendDisplayName = "Friend User";
}
