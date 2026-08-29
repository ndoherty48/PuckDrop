using Xunit;

namespace PuckDrop.Domain.Tests;

/// <summary>
/// One trivial fact confirming the MTP/xUnit v3 test tooling is wired up correctly
/// (project settings, package references, solution/CPM registration). Safe to delete once
/// real Domain tests exist and the tooling is proven.
/// </summary>
public class ToolingSanityCheckTests
{
    [Fact]
    public void DotnetTestRunsUnderMtp()
    {
        Assert.True(true);
    }
}
