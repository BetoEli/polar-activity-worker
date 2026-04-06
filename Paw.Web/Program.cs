using Microsoft.AspNetCore.Authentication.Cookies;
using Paw.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowCredentials().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "paw_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpClient<PawApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PawApi:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("X-QEP-API-Key", builder.Configuration["PawApi:ApiKey"]);
});

// Separate client that does NOT follow redirects — used only for the Polar OAuth proxy
builder.Services.AddHttpClient("PawApiNoRedirect", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PawApi:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("X-QEP-API-Key", builder.Configuration["PawApi:ApiKey"]);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

var app = builder.Build();

app.UseCors();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "paw-web", timestamp = DateTime.UtcNow }));

app.Run();

public partial class Program { }
