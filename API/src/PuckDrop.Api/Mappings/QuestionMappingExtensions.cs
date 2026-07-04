using PuckDrop.Api.Contracts;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Api.Mappings;

public static class QuestionMappingExtensions
{
    public static QuestionResponse ToResponse(this Question question, IReadOnlyList<Option> options) => new(
        question.QuestionId, question.Text, question.SortOrder, question.CorrectOptionId,
        options.Select(o => o.ToResponse()).ToList());
}
