using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Domain.Tests;

public class UserAnswerTests
{
    private static UserAnswer CreateAnswer(string selectedOptionId) => new()
    {
        UserId = "user-1",
        DisplayName = "Nathan",
        QuestionId = "question-1",
        PollId = "poll-1",
        SelectedOptionId = selectedOptionId
    };

    [Fact]
    public void IsCorrect_BeforeEvaluate_IsNull()
    {
        var answer = CreateAnswer("option-1");

        Assert.Null(answer.IsCorrect);
    }

    [Fact]
    public void Evaluate_MatchingOption_SetsIsCorrectTrue()
    {
        var answer = CreateAnswer("option-1");

        answer.Evaluate("option-1");

        Assert.True(answer.IsCorrect);
    }

    [Fact]
    public void Evaluate_NonMatchingOption_SetsIsCorrectFalse()
    {
        var answer = CreateAnswer("option-1");

        answer.Evaluate("option-2");

        Assert.False(answer.IsCorrect);
    }

    [Fact]
    public void Evaluate_CalledTwiceWithDifferentCorrectAnswers_OverwritesResult()
    {
        var answer = CreateAnswer("option-1");

        answer.Evaluate("option-2"); // wrong first pass
        answer.Evaluate("option-1"); // re-scored correct

        Assert.True(answer.IsCorrect);
    }
}
