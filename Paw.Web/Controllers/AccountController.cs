using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Paw.Web.Models;

namespace Paw.Web.Controllers;

[Route("[controller]")]
public class AccountController(IConfiguration config) : Controller
{
    [HttpGet("Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/Dashboard");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (model.Email != config["DevLogin:Email"])
        {
            ModelState.AddModelError(string.Empty, "Invalid email.");
            return View(model);
        }

        // PersonId is an internal identifier — resolved from config, never shown in the UI
        var personId = config["DevLogin:PersonId"] ?? "";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, personId),
            new(ClaimTypes.Email, model.Email),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Redirect(model.ReturnUrl ?? "/Dashboard");
    }

    [Authorize]
    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }
}
