using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Mappings;

public static class ParticipationMappingExtensions
{
    public static SeasonParticipationResponse ToResponse(this SeasonParticipation participation) => new(
        participation.PlayerCount,
        participation.Polls.Select(p => new PollParticipationResponse(p.PollId, p.PickedCount)).ToList());
}
