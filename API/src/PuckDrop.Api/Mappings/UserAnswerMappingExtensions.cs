using PuckDrop.Api.Contracts;
using PuckDrop.Domain.Entities;

namespace PuckDrop.Api.Mappings;

public static class UserAnswerMappingExtensions
{
    public static UserAnswerResponse ToResponse(this UserAnswer answer) => new(
        answer.QuestionId, answer.SelectedOptionId,
        answer.SubmittedAt.ToString("O"), answer.IsCorrect);
}
