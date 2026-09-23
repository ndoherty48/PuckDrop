using PuckDrop.Api.Contracts;
using PuckDrop.Application.Models;

namespace PuckDrop.Api.Mappings;

public static class FixtureImportMappingExtensions
{
    public static FixtureImportItem ToResponse(this FixtureCandidate candidate) => new(
        candidate.Title, candidate.Category,
        candidate.GameDate.ToString("yyyy-MM-dd"), candidate.Deadline.ToString("O"),
        candidate.AlreadyImported);
}
