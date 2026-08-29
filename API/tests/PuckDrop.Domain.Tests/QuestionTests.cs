using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Domain.Tests;

public class QuestionTests
{
    private static Question CreateQuestion() => new()
    {
        QuestionId = "question-1",
        PollId = "poll-1",
        Text = "Who scores first?",
        SortOrder = 0
    };

    [Fact]
    public void IsScored_BeforeCorrectOptionSet_ReturnsFalse()
    {
        var question = CreateQuestion();

        Assert.False(question.IsScored);
        Assert.Null(question.CorrectOptionId);
    }

    [Fact]
    public void SetCorrectOption_ValidId_SetsIdAndMarksScored()
    {
        var question = CreateQuestion();

        question.SetCorrectOption("option-1");

        Assert.Equal("option-1", question.CorrectOptionId);
        Assert.True(question.IsScored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCorrectOption_EmptyOrWhitespaceId_ThrowsArgumentException(string optionId)
    {
        var question = CreateQuestion();

        Assert.Throws<ArgumentException>(() => question.SetCorrectOption(optionId));
    }

    [Fact]
    public void SetCorrectOption_NullId_ThrowsArgumentException()
    {
        var question = CreateQuestion();

        Assert.Throws<ArgumentException>(() => question.SetCorrectOption(null!));
    }
}
