using PuckDrop.Api.Contracts;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Api.Mappings;

public static class SeasonMappingExtensions
{
    public static SeasonResponse ToResponse(this Season season) => new(
        season.SeasonId, season.Name,
        season.StartDate.ToString("yyyy-MM-dd"),
        season.EndDate.ToString("yyyy-MM-dd"));
}
