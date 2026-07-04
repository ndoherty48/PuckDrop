using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using PuckDrop.Domain.Repositories;

namespace PuckDrop.Application.Services;

public class PollService
{
    private readonly IPollRepository _pollRepository;
    private readonly SeasonService _seasonService;

    public PollService(IPollRepository pollRepository, SeasonService seasonService)
    {
        _pollRepository = pollRepository;
        _seasonService = seasonService;
    }

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
        var season = await _seasonService.EnsureSeasonExistsAsync(gameDate, cancellationToken);

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

        await _pollRepository.SavePollAsync(poll, cancellationToken);
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

        await _pollRepository.SavePollAsync(poll, cancellationToken);
        return poll;
    }

    /// <summary>
    /// Publishes a poll (Draft → Open).
    /// </summary>
    public async Task<GameDayPoll> PublishPollAsync(string pollId, CancellationToken cancellationToken = default)
    {
        var poll = await GetPollOrThrowAsync(pollId, cancellationToken);
        poll.Publish();
        await _pollRepository.SavePollAsync(poll, cancellationToken);
        return poll;
    }

    /// <summary>
    /// Manually closes a poll (Open → Closed).
    /// </summary>
    public async Task<GameDayPoll> ClosePollAsync(string pollId, CancellationToken cancellationToken = default)
    {
        var poll = await GetPollOrThrowAsync(pollId, cancellationToken);
        poll.Close();
        await _pollRepository.SavePollAsync(poll, cancellationToken);
        return poll;
    }

    /// <summary>
    /// Gets a poll by ID.
    /// </summary>
    public async Task<GameDayPoll?> GetPollAsync(string pollId, CancellationToken cancellationToken = default)
    {
        return await _pollRepository.GetByIdAsync(pollId, cancellationToken);
    }

    /// <summary>
    /// Gets a poll with all questions and options.
    /// </summary>
    public async Task<(GameDayPoll Poll, IReadOnlyList<Question> Questions, IReadOnlyList<Option> Options)?> GetPollWithQuestionsAsync(
        string pollId, CancellationToken cancellationToken = default)
    {
        return await _pollRepository.GetWithQuestionsAsync(pollId, cancellationToken);
    }

    /// <summary>
    /// Lists polls for a season, optionally filtered by status.
    /// </summary>
    public async Task<IReadOnlyList<GameDayPoll>> ListPollsAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        return await _pollRepository.ListBySeasonAsync(seasonId, cancellationToken);
    }

    /// <summary>
    /// Gets currently open polls for a season.
    /// </summary>
    public async Task<IReadOnlyList<GameDayPoll>> GetActivePollsAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        return await _pollRepository.GetActiveAsync(seasonId, cancellationToken);
    }

    /// <summary>
    /// Adds a question with options to a poll.
    /// </summary>
    public async Task<Question> AddQuestionAsync(
        string pollId,
        string text,
        int sortOrder,
        IReadOnlyList<(string Text, int SortOrder)> options,
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

        await _pollRepository.SaveQuestionAsync(question, optionEntities, cancellationToken);
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

        await _pollRepository.DeleteQuestionAsync(pollId, questionId, cancellationToken);
    }

    private async Task<GameDayPoll> GetPollOrThrowAsync(string pollId, CancellationToken cancellationToken)
    {
        var poll = await _pollRepository.GetByIdAsync(pollId, cancellationToken);
        if (poll is null)
            throw new KeyNotFoundException($"Poll '{pollId}' not found.");
        return poll;
    }
}
