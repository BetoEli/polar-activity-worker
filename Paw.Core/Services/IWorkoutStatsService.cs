using Paw.Core.DTOs;

namespace Paw.Core.Services;

public interface IWorkoutStatsService
{
    Task<WorkoutWeekStats> GetWeekStatsAsync(Guid userId, DateTime? forWeekContaining = null, CancellationToken cancellationToken = default);
}

