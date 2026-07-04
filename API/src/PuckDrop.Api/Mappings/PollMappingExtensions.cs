using PuckDrop.Api.Contracts;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Api.Mappings;

public static class PollMappingExtensions
{
    public static PollResponse ToResponse(this GameDayPoll poll) => new(
        poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
        poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
        poll.CreatedBy, poll.CreatedAt.ToString("O"));

    public static PollDetailResponse ToDetailResponse(
        this GameDayPoll poll,
        IReadOnlyList<Question> questions,
        IReadOnlyList<Option> options)
    {
        var optionsByQuestion = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        var questionResponses = questions.Select(q => q.ToResponse(
            optionsByQuestion.GetValueOrDefault(q.QuestionId, [])
        )).ToList();

        return new PollDetailResponse(
            poll.PollId, poll.SeasonId, poll.GameDate.ToString("yyyy-MM-dd"),
            poll.Title, poll.Deadline.ToString("O"), poll.Status.ToString(),
            poll.CreatedBy, poll.CreatedAt.ToString("O"), questionResponses);
    }
}
