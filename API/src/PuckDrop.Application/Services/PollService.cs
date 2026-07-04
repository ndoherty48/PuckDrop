using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using PuckDrop.Domain.Models;
using PuckDrop.Domain.Repositories;

namespace PuckDrop.Application.Services;

public class PollService(IPollRepository pollRepository, SeasonService seasonService)
{
    /// <summary>
    /// Creates a new poll in Draft status. Auto-creates the season if needed.
    /// </summary>
    public async Task<GameDayPoll> CreatePollAsync(
        string title,
        DateOnly gameDate,
        DateTime deadline,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var season = await seasonService.EnsureSeasonExistsAsync(gameDate, cancellationToken);

        var poll = new GameDayPoll
        {
            PollId = Guid.CreateVersion7().ToString("N"),
            SeasonId = season.SeasonId,
            GameDate = gameDate,
            Title = title,
            Deadline = deadline.ToUniversalTime(),
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        await pollRepository.SavePollAsync(poll, cancellationToken);
        return poll;
    }

    /// <summary>
    /// Updates a poll's title and/or deadline. Only allowed while Draft or Open.
    /// </summary>
    public async Task<GameDayPoll> UpdatePollAsync(
        string pollId,
        string? title,
        DateTime? deadline,
        CancellationToken cancellationToken = default)
    {
        var poll = await GetPollOrThrowAsync(pollId, cancellationToken);

        if (poll.Status is not (PollStatus.Draft or PollStatus.Open))
            throw new InvalidOperationException($"Cannot update a poll with status '{poll.Status}'.");

        if (title is not null)
            poll.Title = title;

        if (deadline.HasValue)
            poll.Deadline = deadline.Value.ToUniversalTime();

        await pollRepository.SavePollAsync(poll, cancellationToken);
        return poll;
    }

    /// <summary>
    /// Publishes a poll (Draft → Open).
    /// </summary>
    public async Task<GameDayPoll> PublishPollAsync(string pollId, CancellationToken cancellationToken = default)
    {
        var poll = await GetPollOrThrowAsync(pollId, cancellationToken);
        poll.Publish();
        await pollRepository.SavePollAsync(poll, cancellationToken);
        return poll;
    }

    /// <summary>
    /// Manually closes a poll (Open → Closed).
    /// </summary>
    public async Task<GameDayPoll> ClosePollAsync(string pollId, CancellationToken cancellationToken = default)
    {
        var poll = await GetPollOrThrowAsync(pollId, cancellationToken);
        poll.Close();
        await pollRepository.SavePollAsync(poll, cancellationToken);
        return poll;
    }

    /// <summary>
    /// Gets a poll by ID.
    /// </summary>
    public async Task<GameDayPoll?> GetPollAsync(string pollId, CancellationToken cancellationToken = default)
    {
        return await pollRepository.GetByIdAsync(pollId, cancellationToken);
    }

    /// <summary>
    /// Gets a poll with all questions and options.
    /// </summary>
    public async Task<PollWithQuestions?> GetPollWithQuestionsAsync(
        string pollId, CancellationToken cancellationToken = default)
    {
        return await pollRepository.GetWithQuestionsAsync(pollId, cancellationToken);
    }

    /// <summary>
    /// Lists polls for a season, optionally filtered by status.
    /// </summary>
    public async Task<IReadOnlyList<GameDayPoll>> ListPollsAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        return await pollRepository.ListBySeasonAsync(seasonId, cancellationToken);
    }

    /// <summary>
    /// Gets currently open polls for a season.
    /// </summary>
    public async Task<IReadOnlyList<GameDayPoll>> GetActivePollsAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        return await pollRepository.GetActiveAsync(seasonId, cancellationToken);
    }

    /// <summary>
    /// Adds a question with options to a poll.
    /// </summary>
    public async Task<Question> AddQuestionAsync(
        string pollId,
        string text,
        int sortOrder,
        IReadOnlyList<OptionDefinition> options,
        CancellationToken cancellationToken = default)
    {
        var poll = await GetPollOrThrowAsync(pollId, cancellationToken);

        if (poll.Status is not (PollStatus.Draft or PollStatus.Open))
            throw new InvalidOperationException($"Cannot add questions to a poll with status '{poll.Status}'.");

        var question = new Question
        {
            QuestionId = Guid.CreateVersion7().ToString("N"),
            PollId = pollId,
            Text = text,
            SortOrder = sortOrder
        };

        var optionEntities = options.Select(o => new Option
        {
            OptionId = Guid.CreateVersion7().ToString("N"),
            QuestionId = question.QuestionId,
            Text = o.Text,
            SortOrder = o.SortOrder
        }).ToList();

        await pollRepository.SaveQuestionAsync(question, optionEntities, cancellationToken);
        return question;
    }

    /// <summary>
    /// Removes a question and its options from a poll.
    /// </summary>
    public async Task DeleteQuestionAsync(string pollId, string questionId, CancellationToken cancellationToken = default)
    {
        var poll = await GetPollOrThrowAsync(pollId, cancellationToken);

        if (poll.Status is not (PollStatus.Draft or PollStatus.Open))
            throw new InvalidOperationException($"Cannot delete questions from a poll with status '{poll.Status}'.");

        await pollRepository.DeleteQuestionAsync(pollId, questionId, cancellationToken);
    }

    private async Task<GameDayPoll> GetPollOrThrowAsync(string pollId, CancellationToken cancellationToken)
    {
        var poll = await pollRepository.GetByIdAsync(pollId, cancellationToken);
        if (poll is null)
            throw new KeyNotFoundException($"Poll '{pollId}' not found.");
        return poll;
    }
}
