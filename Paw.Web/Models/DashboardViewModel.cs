using Paw.Core.DTOs;

namespace Paw.Web.Models;

public class DashboardViewModel
{
    public string DisplayName { get; set; } = "";
    public List<ActivityListItem> RecentActivities { get; set; } = new();
    public WorkoutWeekStats? WeekStats { get; set; }
    public bool HasPolarLinked { get; set; }
}
