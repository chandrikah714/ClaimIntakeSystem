// ============================================================
// FILE: ClaimIntake.Web/Controllers/AccountController.cs
// PURPOSE: Handles Login and Logout.
//
// HOW MVC WORKS (beginner explanation):
// When you visit a URL like /Account/Login:
// 1. ASP.NET looks for AccountController
// 2. Calls the Login() method (called "Action")
// 3. The action returns a View (the HTML page)
// 4. The view is rendered and sent to the browser
// ============================================================

using ClaimIntake.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace ClaimIntake.Web.Controllers;

public class AccountController : Controller
{
    // IConfiguration lets us read from appsettings.json
    private readonly IConfiguration _config;
    private readonly ILogger<AccountController> _logger;

    // Constructor Injection: ASP.NET automatically provides these objects
    // This is called "Dependency Injection" (DI)
    // We ask for what we need, the framework gives it to us
    public AccountController(IConfiguration config,
        ILogger<AccountController> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── GET /Account/Login ───────────────────────────────────────────────────
    // [HttpGet] means: respond to GET requests (when browser navigates to URL)
    [HttpGet]
    public IActionResult Login()
    {
        // If already logged in, skip login page and go directly to claim form
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Submit", "Claim");

        return View(new LoginViewModel());
    }

    // ── POST /Account/Login ──────────────────────────────────────────────────
    // [HttpPost] means: respond to POST requests (when form is submitted)
    [HttpPost]
    [ValidateAntiForgeryToken]  // ← SECURITY: Prevents Cross-Site Request Forgery attacks
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        // ModelState.IsValid checks all the [Required] and [StringLength] rules
        if (!ModelState.IsValid)
            return View(model);  // Return form with error messages shown

        try
        {
            // Look up the user in our database
            var user = await GetUserFromDatabase(model.Username);

            if (user == null)
            {
                // SECURITY TIP: Don't say "username not found" specifically
                // That tells attackers which usernames exist!
                // Always say "invalid username or password"
                _logger.LogWarning(
                    "Login attempt with unknown username: {Username}", model.Username);
                model.ErrorMessage = "Invalid username or password.";
                return View(model);
            }

            if (!user.IsActive)
            {
                model.ErrorMessage = "Your account has been disabled. Contact admin.";
                return View(model);
            }

            // BCrypt.Verify: Compare the typed password against the stored hash
            // Returns true if they match, false if not
            // This is SAFE because BCrypt is a one-way function!
            if (!BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            {
                _logger.LogWarning(
                    "Failed login attempt for username: {Username}", model.Username);
                model.ErrorMessage = "Invalid username or password.";
                return View(model);
            }

            // ── CREATE LOGIN SESSION ─────────────────────────────────────
            // "Claims" here are pieces of information about the logged-in user
            // (not medical claims! "Claim" is overloaded terminology)
            var authClaims = new List<System.Security.Claims.Claim>
            {
                new(ClaimTypes.Name,       user.Username),
                new(ClaimTypes.Role,       user.Role),
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new("LoginTime",           DateTime.UtcNow.ToString("O"))
            };

            var identity = new ClaimsIdentity(authClaims,
                CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Sign in: creates the cookie and sends it to the browser
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,  // Don't remember after browser closes
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            // Write to audit trail
            await WriteAudit(null, "USER_LOGIN", user.Username,
                $"Successful login from IP: {GetClientIp()}");

            _logger.LogInformation("User {Username} logged in successfully.", user.Username);

            // Redirect to claim submission form
            return RedirectToAction("Submit", "Claim");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {Username}", model.Username);
            model.ErrorMessage = "A system error occurred. Please try again.";
            return View(model);
        }
    }

    // ── GET /Account/Logout ──────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name ?? "Unknown";

        // Sign out: clears the cookie from browser
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        await WriteAudit(null, "USER_LOGOUT", username, "User logged out");
        _logger.LogInformation("User {Username} logged out.", username);

        return RedirectToAction("Login");
    }

    // ── PRIVATE HELPER METHODS ───────────────────────────────────────────────

    // A simple User record (no need for a separate class file for this)
    private record UserRecord(int UserId, string Username,
        string PasswordHash, string Role, bool IsActive);

    private async Task<UserRecord?> GetUserFromDatabase(string username)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        const string sql = @"
            SELECT UserId, Username, PasswordHash, Role, IsActive
            FROM   Users
            WHERE  Username = @Username";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Username", username);  // Parameterized = safe!

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new UserRecord(
            UserId: reader.GetInt32(0),
            Username: reader.GetString(1),
            PasswordHash: reader.GetString(2),
            Role: reader.GetString(3),
            IsActive: reader.GetBoolean(4)
        );
    }

    private async Task WriteAudit(string? claimId, string action,
        string performedBy, string details)
    {
        try
        {
            var connStr = _config.GetConnectionString("ClaimsDB")!;
            const string sql = @"
                INSERT INTO AuditTrail (ClaimId, Action, PerformedBy, IPAddress, Details)
                VALUES (@ClaimId, @Action, @PerformedBy, @IPAddress, @Details)";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ClaimId", (object?)claimId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Action", action);
            cmd.Parameters.AddWithValue("@PerformedBy", performedBy);
            cmd.Parameters.AddWithValue("@IPAddress", GetClientIp());
            cmd.Parameters.AddWithValue("@Details", details);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // Audit failure should NOT break the login flow
            _logger.LogError(ex, "Failed to write audit trail.");
        }
    }

    private string GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
}
