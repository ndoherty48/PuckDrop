using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Mappings;

public static class PollResultsMappingExtensions
{
    public static PollResultsResponse ToResponse(this PollResults results)
    {
        var optionsByQuestion = results.Options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        var questionResponses = results.Questions.Select(q => q.ToResponse(
            optionsByQuestion.GetValueOrDefault(q.QuestionId, [])
        )).ToList();

        var userResults = results.UserResults.Select(u => new UserResultResponse(
            u.UserId,
            u.DisplayName,
            u.Answers.Select(a => a.ToResponse()).ToList(),
            u.Points
        )).ToList();

        return new PollResultsResponse(results.Poll.PollId, results.Poll.Status.ToString(), questionResponses, userResults);
    }
}
