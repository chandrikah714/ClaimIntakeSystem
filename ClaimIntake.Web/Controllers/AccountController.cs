// ============================================================
// FILE: ClaimIntake.Web/Controllers/AccountController.cs
// PURPOSE: Authentication - Login, Logout, and session management
// ============================================================

using ClaimIntake.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace ClaimIntake.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AccountController> _logger;

        public AccountController(IConfiguration config, ILogger<AccountController> logger)
        {
            _config = config;
            _logger = logger;
        }

        // ── GET /Account/Login ──────────────────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            // If already logged in, redirect to dashboard
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin"))
                    return RedirectToAction("Dashboard", "Admin");
                else
                    return RedirectToAction("MyClaims", "MyAccount");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel());
        }

        // ── POST /Account/Login ─────────────────────────────────────────────
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    model.ErrorMessage = "Invalid login attempt.";
                    return View(model);
                }

                _logger.LogInformation("Login attempt for user: {Username}", model.Username);

                // Query database for user
                var user = await GetUserFromDatabase(model.Username);

                if (user == null)
                {
                    _logger.LogWarning("Login attempt with unknown username: {Username}", model.Username);
                    model.ErrorMessage = "Invalid username or password.";
                    return View(model);
                }

                _logger.LogInformation("User found in database: {Username}", model.Username);

                if (!user.IsActive)
                {
                    _logger.LogWarning("Login attempt on inactive account: {Username}", model.Username);
                    model.ErrorMessage = "Your account has been disabled. Please contact support.";
                    return View(model);
                }

                _logger.LogInformation("Verifying password for: {Username}", model.Username);

                // Verify password using BCrypt
                bool passwordValid = BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash);
                _logger.LogInformation("Password valid: {Valid}", passwordValid);

                if (!passwordValid)
                {
                    _logger.LogWarning("Failed login attempt for user: {Username}", model.Username);
                    model.ErrorMessage = "Invalid username or password.";
                    return View(model);
                }

                _logger.LogInformation("Authentication successful for: {Username}", model.Username);

                // ── Create authentication claims ────────────────────────────
                var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("LoginTime", DateTime.UtcNow.ToString("O"))
                };

                var claimsIdentity = new ClaimsIdentity(
                    authClaims,
                    CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
                    IssuedUtc = DateTimeOffset.UtcNow,
                    AllowRefresh = true
                };

                // Sign in user
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Write audit trail
                await WriteAudit(null, "USER_LOGIN", user.Username,
                    $"Successful login from IP: {GetClientIp()}");

                _logger.LogInformation("User {Username} logged in successfully. Role: {Role}", user.Username, user.Role);

                // Redirect based on role
                if (user.Role == "Admin")
                    return RedirectToAction("Dashboard", "Admin");
                else
                    return RedirectToAction("MyClaims", "MyAccount");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error during login for {Username}", model.Username);
                model.ErrorMessage = "A database error occurred. Please try again later.";
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login for {Username}", model.Username);
                model.ErrorMessage = "An unexpected error occurred. Please try again.";
                return View(model);
            }
        }

        // ── GET /Account/Logout ─────────────────────────────────────────────
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var username = User.Identity?.Name ?? "Unknown";

            try
            {
                await WriteAudit(null, "USER_LOGOUT", username, "User logged out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing logout audit trail");
            }

            _logger.LogInformation("User {Username} logged out.", username);

            // Sign out and clear authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Login");
        }

        // ── GET /Account/AccessDenied ───────────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ── PRIVATE HELPER METHODS ──────────────────────────────────────────

        private record UserRecord(
            int UserId,
            string Username,
            string PasswordHash,
            string Role,
            bool IsActive);

        private async Task<UserRecord?> GetUserFromDatabase(string username)
        {
            var connStr = _config.GetConnectionString("ClaimsDB");

            if (string.IsNullOrEmpty(connStr))
            {
                _logger.LogError("Connection string 'ClaimsDB' not found in configuration");
                throw new InvalidOperationException("Connection string 'ClaimsDB' not found.");
            }

            _logger.LogInformation("Querying database for user: {Username}", username);

            const string sql = @"
                SELECT UserId, Username, PasswordHash, Role, IsActive
                FROM Users
                WHERE Username = @Username";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Username", username);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                _logger.LogWarning("User not found in database: {Username}", username);
                return null;
            }

            var record = new UserRecord(
                UserId: reader.GetInt32(0),
                Username: reader.GetString(1),
                PasswordHash: reader.GetString(2),
                Role: reader.GetString(3),
                IsActive: reader.GetBoolean(4)
            );

            _logger.LogInformation("User record retrieved: {Username}, Role: {Role}, Active: {Active}",
                record.Username, record.Role, record.IsActive);

            return record;
        }

        private async Task WriteAudit(string? claimId, string action,
            string performedBy, string details)
        {
            try
            {
                var connStr = _config.GetConnectionString("ClaimsDB");

                if (string.IsNullOrEmpty(connStr))
                {
                    _logger.LogError("Connection string not found for audit trail");
                    return;
                }

                const string sql = @"
                    INSERT INTO AuditTrail (ClaimId, Action, PerformedBy, IPAddress, Details, CreatedAt)
                    VALUES (@ClaimId, @Action, @PerformedBy, @IPAddress, @Details, GETUTCDATE())";

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
                _logger.LogError(ex, "Failed to write audit trail for action: {Action}", action);
                // Don't throw - audit failure shouldn't break login flow
            }
        }

        private string GetClientIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
    }
}
