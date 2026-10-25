using PuckDrop.Api.Contracts;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;

namespace PuckDrop.Api.Mappings;

public static class PollMappingExtensions
{
    /// <param name="includeAdminDetail">
    /// Whether to say who cancelled a cancelled poll and when - admins only.
    /// </param>
    public static PollResponse ToResponse(this GameDayPoll poll, bool includeAdminDetail = false) => new(
        poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
        poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
        poll.CreatedBy, poll.CreatedAt.ToString("O"), poll.ToCancellationResponse(includeAdminDetail));

    /// <param name="includeAdminDetail">
    /// Whether to say who cancelled a cancelled poll and when - admins only.
    /// </param>
    public static PollDetailResponse ToDetailResponse(
        this GameDayPoll poll,
        IReadOnlyList<Question> questions,
        IReadOnlyList<Option> options,
        bool includeAdminDetail = false)
    {
        var optionsByQuestion = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        var questionResponses = questions.Select(q => q.ToResponse(
            optionsByQuestion.GetValueOrDefault(q.QuestionId, [])
        )).ToList();

        return new PollDetailResponse(
            poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
            poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
            poll.CreatedBy, poll.CreatedAt.ToString("O"), questionResponses,
            poll.ToCancellationResponse(includeAdminDetail));
    }

    private static PollCancellationResponse? ToCancellationResponse(this GameDayPoll poll, bool includeAdminDetail)
    {
        if (poll.Status != PollStatus.Cancelled)
            return null;

        return new PollCancellationResponse(
            poll.CancellationReason!,
            poll.CancelledFromStatus!.Value.ToString(),
            includeAdminDetail ? poll.CancelledByName : null,
            includeAdminDetail ? poll.CancelledAt!.Value.ToString("O") : null);
    }
}
