// ============================================================
// FILE: ClaimIntake.Web/Program.cs
// PURPOSE: Application startup and configuration
// ============================================================

using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ── ADD SERVICES ─────────────────────────────────────────────────────────────

// Controllers with Views (enables MVC pattern)
builder.Services.AddControllersWithViews();

// HTTP Client for external APIs
builder.Services.AddHttpClient();

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "ClaimIntakeAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

// Authorization
builder.Services.AddAuthorization();

// HTTP Context Accessor
builder.Services.AddHttpContextAccessor();

// ── BUILD APP ────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── CONFIGURE MIDDLEWARE ────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Uncomment for HTTPS enforcement in production
// app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ── MAP ROUTES ───────────────────────────────────────────────────────────────

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapFallback(() => Results.Redirect("/Account/Login"));

// ── RUN ──────────────────────────────────────────────────────────────────────

app.Run();
