using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Paw.Web.Models;
using Paw.Web.Services;

namespace Paw.Web.Controllers;

[Authorize]
[Route("[controller]")]
public class DashboardController(PawApiClient api) : Controller
{
    [HttpGet]
    [Route("")]
    [Route("~/")]  // also handle root "/"
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var personId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";

        var activitiesTask = api.GetActivitiesAsync(personId, limit: 50, ct);
        var statsTask = api.GetWeekStatsAsync(personId, ct: ct);
        var linkedTask = api.HasPolarLinkAsync(personId, ct);

        await Task.WhenAll(activitiesTask, statsTask, linkedTask);

        var vm = new DashboardViewModel
        {
            PersonId = personId,
            Email = email,
            RecentActivities = await activitiesTask,
            WeekStats = await statsTask,
            HasPolarLinked = await linkedTask,
        };

        return View(vm);
    }
}
