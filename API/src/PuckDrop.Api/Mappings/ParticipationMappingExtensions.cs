using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Mappings;

public static class ParticipationMappingExtensions
{
    public static SeasonParticipationResponse ToResponse(this SeasonParticipation participation) => new(
        participation.PlayerCount,
        participation.Polls.Select(p => new PollParticipationResponse(p.PollId, p.PickedCount)).ToList());

    public static PollParticipationDetailResponse ToResponse(this PollParticipationDetail participation) => new(
        participation.PollId,
        participation.PickedCount,
        participation.PlayerCount,
        participation.StillToPick.Select(p => new PlayerResponse(p.UserId, p.DisplayName)).ToList());
}
