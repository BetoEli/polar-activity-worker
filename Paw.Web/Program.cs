using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Tokens;
using Sustainsys.Saml2;
using Sustainsys.Saml2.AspNetCore2;
using Sustainsys.Saml2.Metadata;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Sustainsys.Saml2.Metadata;
var builder = WebApplication.CreateBuilder(args);

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins)
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configure Authentication - DISABLED FOR DEVELOPMENT
// Uncomment the section below to enable SAML/JWT authentication in production
/*
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Saml2Defaults.Scheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "paw_session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
})
.AddSaml2(options =>
{
    var samlConfig = builder.Configuration.GetSection("Saml");
    
    options.SPOptions.EntityId = new EntityId(samlConfig["SpEntityId"]!);
    options.SPOptions.ReturnUrl = new Uri(samlConfig["ReturnUrl"]!);
    
    var idpEntityId = samlConfig["IdpEntityId"];
    var idpMetadataUrl = samlConfig["IdpMetadataUrl"];
    
    if (!string.IsNullOrEmpty(idpEntityId) && !string.IsNullOrEmpty(idpMetadataUrl))
    {
        var idp = new IdentityProvider(
            new EntityId(idpEntityId),
            options.SPOptions)
        {
            MetadataLocation = idpMetadataUrl,
            LoadMetadata = true
        };
        
        options.IdentityProviders.Add(idp);
    }
});

builder.Services.AddAuthorization();
*/

var app = builder.Build();

app.UseCors();
app.UseHttpsRedirection();

// Authentication middleware - DISABLED FOR DEVELOPMENT
// app.UseAuthentication();
// app.UseAuthorization();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "paw-web", timestamp = DateTime.UtcNow }));

// SAML Login - initiate SAML flow - DISABLED FOR DEVELOPMENT
app.MapGet("/auth/login", async (HttpContext ctx) =>
{
    // Return development mode message
    return Results.Ok(new { message = "Authentication disabled in development mode" });
});

// SAML callback - mint JWT
app.MapGet("/auth/complete", (HttpContext ctx, IConfiguration config, ILogger<Program> logger) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true)
    {
        logger.LogWarning("User not authenticated in /auth/complete, redirecting to login");
        return Results.Redirect("/auth/login");
    }

    // Log all claims for debugging
    logger.LogInformation("=== SAML Claims received ===");
    foreach (var claim in ctx.User.Claims)
    {
        logger.LogInformation("  {Type}: {Value}", claim.Type, claim.Value);
    }
    logger.LogInformation("=========================");

    // Extract claims (try for multiple claim types)
    var sub = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? ctx.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
           ?? ctx.User.FindFirst(ClaimTypes.Upn)?.Value
           ?? ctx.User.FindFirst("upn")?.Value
           ?? "unknown";

    var email = ctx.User.FindFirst(ClaimTypes.Email)?.Value
             ?? ctx.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value
             ?? ctx.User.FindFirst("email")?.Value
             ?? "";

    var name = ctx.User.FindFirst(ClaimTypes.Name)?.Value
            ?? ctx.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/displayname")?.Value
            ?? ctx.User.FindFirst("name")?.Value
            ?? email;

    var role = ctx.User.FindFirst(ClaimTypes.Role)?.Value
            ?? ctx.User.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
            ?? ctx.User.FindFirst("role")?.Value
            ?? "Student"; // Default role

    logger.LogInformation("Extracted claims - Sub: {Sub}, Email: {Email}, Name: {Name}, Role: {Role}", 
        sub, email, name, role);

    // Mint JWT
    var jwtConfig = config.GetSection("Jwt");
    var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig["SigningKey"]!));
    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, sub),
        new Claim(JwtRegisteredClaimNames.Email, email),
        new Claim(JwtRegisteredClaimNames.Name, name),
        new Claim("role", role),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
    };

    var token = new JwtSecurityToken(
        issuer: jwtConfig["Issuer"],
        audience: jwtConfig["Audience"],
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(int.Parse(jwtConfig["ExpirationMinutes"] ?? "30")),
        signingCredentials: credentials
    );

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    // Set JWT in HttpOnly cookie
    ctx.Response.Cookies.Append("paw_at", jwt, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UtcNow.AddMinutes(int.Parse(jwtConfig["ExpirationMinutes"] ?? "30"))
    });

    logger.LogInformation("JWT issued for user: {Sub} ({Email}) with role: {Role}", sub, email, role);

    // Return JSON response for debugging or redirect to frontend
    return Results.Ok(new
    {
        message = "Authentication successful",
        user = new { sub, email, name, role },
        token = jwt // Remove this in production!
    });
});
// Note: Authorization disabled for development

// Logout
app.MapPost("/auth/logout", async (HttpContext ctx, ILogger<Program> logger) =>
{
    var sub = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
    
    ctx.Response.Cookies.Delete("paw_at");
    ctx.Response.Cookies.Delete("paw_session");
    
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    
    logger.LogInformation("User logged out: {Sub}", sub);
    
    return Results.Ok(new { message = "Logged out successfully" });
});

// Debug endpoint - current user info
app.MapGet("/auth/me", (HttpContext ctx) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true)
    {
        return Results.Unauthorized();
    }

    var sub = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
    var email = ctx.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
    var name = ctx.User.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
    var role = ctx.User.FindFirst("role")?.Value;

    return Results.Ok(new
    {
        authenticated = true,
        sub,
        email,
        name,
        role,
        claims = ctx.User.Claims.Select(c => new { c.Type, c.Value }).ToList()
    });
});
// Note: Authorization disabled for development

// Serve static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

// Make Program class accessible for testing
public partial class Program { }


