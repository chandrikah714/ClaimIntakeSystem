// ============================================================
// FILE: ClaimIntake.Web/Program.cs
// PURPOSE: This is the ENTRY POINT of our web application.
//          It runs first, sets everything up, then starts the server.
//
// BEGINNER ANALOGY: Think of this as the "opening routine" of
// a restaurant. Before customers arrive, you:
// 1. Set up the kitchen (configure services)
// 2. Unlock the doors (configure the HTTP pipeline)
// 3. Open for business (app.Run())
// ============================================================

using Microsoft.AspNetCore.Authentication.Cookies;

// ── STEP 1: Create a builder (prepares all the ingredients) ──────────────────
var builder = WebApplication.CreateBuilder(args);

// ── STEP 2: Add Services (tell the app what features we want) ────────────────

// MVC: Enables Controllers and Views (the M-V-C pattern)
// Model = data, View = HTML page, Controller = logic
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

// Cookie Authentication: Users log in, get a cookie, cookie proves who they are
// Think of it like getting a wristband at an event - you show it to get back in
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";    // Where to go if not logged in
        options.LogoutPath = "/Account/Logout";   // Where to go to log out
        options.AccessDeniedPath = "/Account/Login";    // Where to go if not authorized
        options.ExpireTimeSpan = TimeSpan.FromHours(8); // Session lasts 8 hours
        options.SlidingExpiration = true;               // Resets timer on activity
        options.Cookie.HttpOnly = true;   // JS can't read cookie (prevents XSS attacks)
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Strict;  // Prevents CSRF attacks
    });

// Authorization: Enforce the [Authorize] attribute on controllers
builder.Services.AddAuthorization();

// HttpContextAccessor: Lets us access the current user info anywhere
builder.Services.AddHttpContextAccessor();

// ── STEP 3: Build the app ─────────────────────────────────────────────────────
var app = builder.Build();

// ── STEP 4: Configure the HTTP Pipeline ──────────────────────────────────────
// The pipeline is like an assembly line. Each request passes through
// each "middleware" in order, from top to bottom.

// Show friendly error pages in development, strict ones in production
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();  // HTTP Strict Transport Security (forces HTTPS)
}

// Force HTTPS: If someone visits http://, redirect to https://
//app.UseHttpsRedirection();

// Static files: Serve CSS, JavaScript, images from wwwroot folder
app.UseStaticFiles();

// Routing: Figure out which controller/action to call based on URL
app.UseRouting();

// Authentication: Check if the user's login cookie is valid
// MUST come before UseAuthorization!
app.UseAuthentication();

// Authorization: Check if the logged-in user has permission
app.UseAuthorization();

// Define URL routes:
// Default: goes to HomeController.Index() if no other route matches
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");
// We default to the Login page, not Home!

// ── STEP 5: Run the app (start listening for requests) ───────────────────────
app.Run();
// The app is now running and waiting for users to visit!
