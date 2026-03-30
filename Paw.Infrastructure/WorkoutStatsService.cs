using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Paw.Core.Domain;
using Paw.Core.DTOs;
using Paw.Core.Services;

namespace Paw.Infrastructure;

public class WorkoutStatsService : IWorkoutStatsService
{
    private readonly PawDbContext _db;
    private readonly ILogger<WorkoutStatsService> _logger;

    public WorkoutStatsService(PawDbContext db, ILogger<WorkoutStatsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<WorkoutWeekStats> GetWeekStatsAsync(
        Guid userId,
        DateTime? forWeekContaining = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("GetWeekStatsAsync is not supported with QEPTest database structure. Activities table does not exist in QEPTest.");
        
        // Note: This service relies on the Activities table which is not in QEPTest
        // If workout stats are needed, create a separate database context for PAW tables
        // or query activity data from QEP Web App's database
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        // Get Sunday of the week containing 'date'
        var daysSinceSunday = (int)date.DayOfWeek;

        return date.Date.AddDays(-daysSinceSunday);
    }
}

