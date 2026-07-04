using Microsoft.AspNetCore.Mvc;
using PuckDrop.Api.Contracts;
using PuckDrop.Application.Services;

namespace PuckDrop.Api.Controllers;

[ApiController]
[Route("polls/{pollId}")]
public class ResultsController(ResultsService resultsService) : ControllerBase
{
    [HttpGet("results")]
    public async Task<ActionResult<PollResultsResponse>> GetResults(string pollId, CancellationToken ct)
    {
        var results = await resultsService.GetPollResultsAsync(pollId, ct);

        var optionsByQuestion = results.Options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        var questionResponses = results.Questions.Select(q => new QuestionResponse(
            q.QuestionId, q.Text, q.SortOrder, q.CorrectOptionId,
            optionsByQuestion.GetValueOrDefault(q.QuestionId, [])
                .Select(o => new OptionResponse(o.OptionId, o.Text, o.SortOrder)).ToList()
        )).ToList();

        var userResults = results.UserResults.Select(u => new UserResultResponse(
            u.UserId,
            u.UserId, // TODO: Resolve display name from Cognito in Phase 5
            u.Answers.Select(a => new UserAnswerResponse(
                a.QuestionId, a.SelectedOptionId, a.SubmittedAt.ToString("O"), a.IsCorrect
            )).ToList(),
            u.Points
        )).ToList();

        return Ok(new PollResultsResponse(results.Poll.PollId, results.Poll.Status.ToString(), questionResponses, userResults));
    }
}
