using PuckDrop.Domain.Entities;

namespace PuckDrop.Application.Repositories;

public interface ISeasonRepository
{
    Task<Season?> GetByIdAsync(string seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Season>> ListAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Season season, CancellationToken cancellationToken = default);
}
