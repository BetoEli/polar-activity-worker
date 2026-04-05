using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Paw.Web.Services;

namespace Paw.Web.Controllers;

[Authorize]
[Route("[controller]")]
public class PolarController(PawApiClient api, ILogger<PolarController> logger) : Controller
{
    [HttpGet("Connect")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        var personId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";

        var polarUrl = await api.GetPolarConnectUrlAsync(personId, email, ct);

        if (string.IsNullOrEmpty(polarUrl))
        {
            logger.LogError("Failed to get Polar OAuth URL for PersonId={PersonId}", personId);
            return RedirectToAction("Connected", new { status = "error", message = "Could not initiate Polar connection." });
        }

        return Redirect(polarUrl);
    }

    // Landing page after Paw.Api OAuth callback redirects here
    [AllowAnonymous]
    [HttpGet("Connected")]
    public IActionResult Connected(string? status, string? email, string? message)
    {
        ViewBag.Status = status ?? "unknown";
        ViewBag.Email = email;
        ViewBag.Message = message;
        return View();
    }
}
