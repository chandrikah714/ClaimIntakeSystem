// ============================================================
// FILE: ClaimIntake.Web/Controllers/AdminController.cs (UPDATED)
// PURPOSE: Admin-only claim management with role verification
// ============================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaimIntake.Web.Models;

namespace ClaimIntake.Web.Controllers;

[Authorize(Roles = "Admin")]  // Only admins can access
[Route("Admin")]
public class AdminController : BaseController
{
    private readonly IConfiguration _config;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IConfiguration config, ILogger<AdminController> logger)
        : base(config)
    {
        _config = config;
        _logger = logger;
    }

    // ── GET /Admin/Dashboard ─────────────────────────────────────────────────
    [HttpGet("Dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        try
        {
            var stats = await GetDashboardStats();
            return View(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading admin dashboard");
            TempData["ErrorMessage"] = "Error loading dashboard. Please try again.";
            return View(new AdminDashboardViewModel());
        }
    }

    // ── GET /Admin/AllClaims ─────────────────────────────────────────────────
    [HttpGet("AllClaims")]
    public async Task<IActionResult> AllClaims(
        string? statusFilter = null,
        string? searchTerm = null,
        int page = 1,
        int pageSize = 20)
    {
        try
        {
            var claims = await GetAllClaims(statusFilter, searchTerm, page, pageSize);
            return View(claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading claims list");
            TempData["ErrorMessage"] = "Error loading claims. Please try again.";
            return View(new AllClaimsViewModel());
        }
    }

    // ── GET /Admin/ClaimDetails/{claimId} ────────────────────────────────────
    [HttpGet("ClaimDetails/{claimId}")]
    public async Task<IActionResult> ClaimDetails(string claimId)
    {
        try
        {
            var claim = await GetClaimDetailsAsync(claimId);
            if (claim == null)
            {
                _logger.LogWarning("Admin {Admin} attempted to access non-existent claim {ClaimId}",
                    User.Identity!.Name, claimId);
                return NotFound();
            }
            return View(claim);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading claim details for {ClaimId}", claimId);
            TempData["ErrorMessage"] = "Error loading claim details.";
            return RedirectToAction("AllClaims");
        }
    }

    // ── POST /Admin/UpdateClaimStatus ────────────────────────────────────────
    // IMPORTANT: Only admins can update claim status
    [HttpPost("UpdateClaimStatus")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateClaimStatus(
        string claimId,
        string newStatus,
        string? notes = null)
    {
        try
        {
            // Verify user is admin
            if (!User.IsInRole("Admin"))
            {
                _logger.LogWarning("Non-admin user {User} attempted to update claim status",
                    User.Identity!.Name);
                return Json(new { success = false, message = "Unauthorized. Only admins can update claim status." });
            }

            // Validate status
            var validStatuses = new[] { "Pending", "Approved", "Rejected" };
            if (!validStatuses.Contains(newStatus))
            {
                return Json(new { success = false, message = "Invalid status." });
            }

            var connStr = _config.GetConnectionString("ClaimsDB")!;
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Update claim status
            const string updateSql = @"
                UPDATE ClaimHeader
                SET ClaimStatus = @Status, UpdatedAt = @UpdatedAt
                WHERE ClaimId = @ClaimId";

            using var updateCmd = new SqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("@Status", newStatus);
            updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
            updateCmd.Parameters.AddWithValue("@ClaimId", claimId);

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Json(new { success = false, message = "Claim not found." });
            }

            // Add to status history
            const string historySql = @"
                INSERT INTO ClaimStatusHistory (ClaimId, Status, ChangedBy, Notes, ChangedAt)
                VALUES (@ClaimId, @Status, @ChangedBy, @Notes, @ChangedAt)";

            using var histCmd = new SqlCommand(historySql, conn);
            histCmd.Parameters.AddWithValue("@ClaimId", claimId);
            histCmd.Parameters.AddWithValue("@Status", newStatus);
            histCmd.Parameters.AddWithValue("@ChangedBy", User.Identity!.Name ?? "Admin");
            histCmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            histCmd.Parameters.AddWithValue("@ChangedAt", DateTime.UtcNow);
            await histCmd.ExecuteNonQueryAsync();

            // Log audit trail
            await WriteAudit(claimId, "CLAIM_STATUS_UPDATED", User.Identity!.Name!,
                $"Status changed to: {newStatus}. Notes: {notes ?? "None"}");

            await SendStatusChangeNotificationAsync(claimId, newStatus, User.Identity!.Name!);

            _logger.LogInformation("Admin {Admin} updated claim {ClaimId} status to {Status}",
                User.Identity!.Name, claimId, newStatus);

            return Json(new { success = true, message = "Claim status updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating claim status for {ClaimId}", claimId);
            return Json(new { success = false, message = "An error occurred while updating the claim status." });
        }
    }

    // ── GET /Admin/UserManagement ────────────────────────────────────────────
    [HttpGet("UserManagement")]
    public async Task<IActionResult> UserManagement()
    {
        try
        {
            var users = await GetAllUsers();
            return View(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading user management page");
            TempData["ErrorMessage"] = "Error loading users.";
            return View(new List<UserViewModel>());
        }
    }

    // ── GET /Admin/RegisterUser ─────────────────────────────────────────────
    [HttpGet("RegisterUser")]
    public IActionResult RegisterUser()
    {
        return View(new AdminUserRegisterViewModel());
    }

    // ── POST /Admin/RegisterUser ────────────────────────────────────────────
    [HttpPost("RegisterUser")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterUser(AdminUserRegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            // Check username uniqueness
            if (await UserExistsAsync(model.Username))
            {
                model.ErrorMessage = $"Username '{model.Username}' is already taken.";
                return View(model);
            }

            // Check email uniqueness
            if (await EmailExistsAsync(model.Email))
            {
                model.ErrorMessage = $"Email '{model.Email}' is already registered.";
                return View(model);
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
            var connStr = _config.GetConnectionString("ClaimsDB")!;

            const string sql = @"
            INSERT INTO Users
                (Username, PasswordHash, Role, IsActive, Email, FullName,
                 Department, PhoneNumber, CreatedAt, CreatedBy)
            VALUES
                (@Username, @PasswordHash, @Role, 1, @Email, @FullName,
                 @Department, @PhoneNumber, @CreatedAt, @CreatedBy)";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Username", model.Username);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
            cmd.Parameters.AddWithValue("@Role", model.Role);
            cmd.Parameters.AddWithValue("@Email", model.Email);
            cmd.Parameters.AddWithValue("@FullName", model.FullName);
            cmd.Parameters.AddWithValue("@Department", (object?)model.Department ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PhoneNumber", (object?)model.PhoneNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@CreatedBy", User.Identity!.Name ?? "System");
            await cmd.ExecuteNonQueryAsync();

            // Insert welcome notification for the new user
            await CreateWelcomeNotificationAsync(model.Username, model.FullName, connStr);

            // Optionally send welcome email
            if (model.SendWelcomeEmail)
            {
                await SendWelcomeEmailAsync(model.Email, model.FullName, model.Username, model.Role);
            }

            await WriteAudit(null, "USER_REGISTERED", User.Identity!.Name!,
                $"New user: {model.Username} ({model.Email}), Role: {model.Role}");

            _logger.LogInformation("Admin {Admin} registered user {Username} ({Email})",
                User.Identity!.Name, model.Username, model.Email);

            TempData["SuccessMessage"] = $"User '{model.Username}' ({model.Email}) registered successfully!";
            return RedirectToAction("UserManagement");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering user");
            model.ErrorMessage = "An unexpected error occurred during registration.";
            return View(model);
        }
    }

    // ── NEW HELPER: Check email uniqueness ───────────────────────
    private async Task<bool> EmailExistsAsync(string email)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        const string sql = "SELECT COUNT(1) FROM Users WHERE Email = @Email";
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Email", email);
        return (int)(await cmd.ExecuteScalarAsync() ?? 0) > 0;
    }

    // ── NEW HELPER: Create welcome notification ──────────────────
    private async Task CreateWelcomeNotificationAsync(string username, string fullName, string connStr)
    {
        try
        {
            // Get new user's ID
            const string getIdSql = "SELECT UserId FROM Users WHERE Username = @Username";
            await using var conn1 = new SqlConnection(connStr);
            await conn1.OpenAsync();
            await using var cmd1 = new SqlCommand(getIdSql, conn1);
            cmd1.Parameters.AddWithValue("@Username", username);
            var userId = (int)(await cmd1.ExecuteScalarAsync() ?? 0);

            if (userId == 0) return;

            const string notifSql = @"
            INSERT INTO Notifications (UserId, Title, Message, Type, ActionUrl)
            VALUES (@UserId, @Title, @Message, 'Info', '/Claim/Submit')";

            await using var conn2 = new SqlConnection(connStr);
            await conn2.OpenAsync();
            await using var cmd2 = new SqlCommand(notifSql, conn2);
            cmd2.Parameters.AddWithValue("@UserId", userId);
            cmd2.Parameters.AddWithValue("@Title", $"Welcome to Claims Portal, {fullName}!");
            cmd2.Parameters.AddWithValue("@Message", "Your account is ready. Start by submitting your first claim.");
            await cmd2.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create welcome notification for {Username}", username);
        }
    }

    // ── NEW HELPER: Send welcome email (needs IEmailService) ─────
    // Add this once you configure SMTP in appsettings.json
    private async Task SendWelcomeEmailAsync(string toEmail, string fullName,
        string username, string role)
    {
        try
        {
            // Replace with your actual email service call
            // Example using MailKit / SmtpClient / SendGrid / etc.
            _logger.LogInformation(
                "Welcome email would be sent to {Email} for user {Username}",
                toEmail, username);
            await Task.CompletedTask; // placeholder
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to {Email}", toEmail);
        }
    }

    // ── POST /Admin/DeactivateUser ──────────────────────────────────────────
    [HttpPost("DeactivateUser/{userId}")]
    public async Task<IActionResult> DeactivateUser(int userId)
    {
        try
        {
            // Prevent deactivating the current admin
            var currentUserSql = "SELECT UserId FROM Users WHERE Username = @Username";
            var connStr = _config.GetConnectionString("ClaimsDB")!;

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var currentCmd = new SqlCommand(currentUserSql, conn);
            currentCmd.Parameters.AddWithValue("@Username", User.Identity!.Name!);
            var currentUserId = (int?)await currentCmd.ExecuteScalarAsync();

            if (currentUserId == userId)
            {
                return Json(new { success = false, message = "Cannot deactivate your own account." });
            }

            const string sql = "UPDATE Users SET IsActive = 0 WHERE UserId = @UserId";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Json(new { success = false, message = "User not found." });
            }

            await WriteAudit(null, "USER_DEACTIVATED", User.Identity!.Name!,
                $"User ID {userId} has been deactivated");

            _logger.LogInformation("Admin {Admin} deactivated user {UserId}",
                User.Identity!.Name, userId);

            return Json(new { success = true, message = "User deactivated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating user {UserId}", userId);
            return Json(new { success = false, message = "Error deactivating user." });
        }
    }

    // ── PRIVATE HELPER METHODS ───────────────────────────────────────────────

    private async Task<AdminDashboardViewModel> GetDashboardStats()
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        var stats = new AdminDashboardViewModel();

        const string statsSql = @"
            SELECT
                COUNT(*) as TotalClaims,
                SUM(CASE WHEN ClaimStatus = 'Pending' THEN 1 ELSE 0 END) as PendingClaims,
                SUM(CASE WHEN ClaimStatus = 'Approved' THEN 1 ELSE 0 END) as ApprovedClaims,
                SUM(CASE WHEN ClaimStatus = 'Rejected' THEN 1 ELSE 0 END) as RejectedClaims,
                SUM(CAST(ClaimAmount as DECIMAL(18,2))) as TotalAmount
            FROM ClaimHeader";

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(statsSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                stats.TotalClaims = reader.GetInt32(0);
                stats.PendingClaims = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                stats.ApprovedClaims = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                stats.RejectedClaims = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                stats.TotalAmount = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
            }
        }

        const string userCountSql = "SELECT COUNT(*) FROM Users WHERE IsActive = 1";
        await using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(userCountSql, conn);
            stats.ActiveUsers = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        return stats;
    }

    private async Task<AllClaimsViewModel> GetAllClaims(
        string? statusFilter, string? searchTerm, int page, int pageSize)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        var claims = new List<ClaimSummaryViewModel>();
        var whereClauses = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrEmpty(statusFilter))
        {
            whereClauses.Add("ClaimStatus = @Status");
            parameters.Add(new SqlParameter("@Status", statusFilter));
        }

        if (!string.IsNullOrEmpty(searchTerm))
        {
            whereClauses.Add("(ClaimId LIKE @SearchTerm OR MemberId LIKE @SearchTerm OR ProviderId LIKE @SearchTerm)");
            parameters.Add(new SqlParameter("@SearchTerm", $"%{searchTerm}%"));
        }

        var whereClause = whereClauses.Any() ? "WHERE " + string.Join(" AND ", whereClauses) : "";
        var sql = $@"
            SELECT ClaimId, MemberId, ProviderId, ClaimAmount, ClaimStatus, SubmittedAt, SubmittedBy
            FROM ClaimHeader
            {whereClause}
            ORDER BY SubmittedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        parameters.Add(new SqlParameter("@Offset", (page - 1) * pageSize));
        parameters.Add(new SqlParameter("@PageSize", pageSize));

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            claims.Add(new ClaimSummaryViewModel
            {
                ClaimId = reader.GetString(0),
                MemberId = reader.GetString(1),
                ProviderId = reader.GetString(2),
                ClaimAmount = reader.GetDecimal(3),
                Status = reader.GetString(4),
                SubmittedAt = reader.GetDateTime(5),
                SubmittedBy = reader.GetString(6)
            });
        }

        return new AllClaimsViewModel
        {
            Claims = claims,
            CurrentPage = page,
            PageSize = pageSize,
            StatusFilter = statusFilter,
            SearchTerm = searchTerm
        };
    }

    private async Task<AdminClaimDetailViewModel?> GetClaimDetailsAsync(string claimId)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;

        const string sql = @"
            SELECT ClaimId, MemberId, ProviderId, DiagnosisCode, ClaimAmount,
                   ClaimStatus, SubmittedAt, SubmittedBy, ProcessedAt
            FROM ClaimHeader
            WHERE ClaimId = @ClaimId";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ClaimId", claimId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var claim = new AdminClaimDetailViewModel
        {
            ClaimId = reader.GetString(0),
            MemberId = reader.GetString(1),
            ProviderId = reader.GetString(2),
            DiagnosisCode = reader.GetString(3),
            ClaimAmount = reader.GetDecimal(4),
            Status = reader.GetString(5),
            SubmittedAt = reader.GetDateTime(6),
            SubmittedBy = reader.GetString(7),
            ProcessedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
        };

        const string historySql = @"
            SELECT Status, ChangedBy, ChangedAt, Notes
            FROM ClaimStatusHistory
            WHERE ClaimId = @ClaimId
            ORDER BY ChangedAt DESC";

        await using var histCmd = new SqlCommand(historySql, conn);
        histCmd.Parameters.AddWithValue("@ClaimId", claimId);
        await using var histReader = await histCmd.ExecuteReaderAsync();

        while (await histReader.ReadAsync())
        {
            claim.StatusHistory.Add(new StatusHistoryViewModel
            {
                Status = histReader.GetString(0),
                ChangedBy = histReader.GetString(1),
                ChangedAt = histReader.GetDateTime(2),
                Notes = histReader.IsDBNull(3) ? null : histReader.GetString(3)
            });
        }

        return claim;
    }

    private async Task<List<UserViewModel>> GetAllUsers()
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        var users = new List<UserViewModel>();

        const string sql = @"
        SELECT UserId, Username, Role, IsActive, CreatedAt, CreatedBy,
               Email, FullName, Department, PhoneNumber, AvatarColor
        FROM Users
        ORDER BY IsActive DESC, CreatedAt DESC";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add(new UserViewModel
            {
                UserId = reader.GetInt32(0),
                Username = reader.GetString(1),
                Role = reader.GetString(2),
                IsActive = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4),
                CreatedBy = reader.GetString(5),
                Email = reader.IsDBNull(6) ? null : reader.GetString(6),
                FullName = reader.IsDBNull(7) ? null : reader.GetString(7),
                Department = reader.IsDBNull(8) ? null : reader.GetString(8),
                PhoneNumber = reader.IsDBNull(9) ? null : reader.GetString(9),
                AvatarColor = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }
        return users;
    }
    private async Task<bool> UserExistsAsync(string username)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        const string sql = "SELECT COUNT(1) FROM Users WHERE Username = @Username";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Username", username);

        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        return count > 0;
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
            cmd.Parameters.AddWithValue("@IPAddress", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
            cmd.Parameters.AddWithValue("@Details", details);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit trail.");
        }
    }
    private async Task SendStatusChangeNotificationAsync(
    string claimId, string newStatus, string changedBy)
    {
        try
        {
            var connStr = _config.GetConnectionString("ClaimsDB")!;
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("sp_NotifyClaimStatusChange", conn)
            {
                CommandType = System.Data.CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ClaimId", claimId);
            cmd.Parameters.AddWithValue("@NewStatus", newStatus);
            cmd.Parameters.AddWithValue("@ChangedBy", changedBy);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send status change notification");
        }
    }

    // ── POST /Admin/UpdateUser ────────────────────────────────────
    [HttpPost("UpdateUser")]
    public async Task<IActionResult> UpdateUser(
        int userId,
        string fullName,
        string email,
        string? department,
        string role)
    {
        try
        {
            if (!User.IsInRole("Admin"))
                return Json(new { success = false, message = "Unauthorized." });

            // Validate email format
            if (!IsValidEmail(email))
                return Json(new { success = false, message = "Invalid email format." });

            // Check email isn't taken by another user
            var connStr = _config.GetConnectionString("ClaimsDB")!;
            const string checkSql =
                "SELECT COUNT(1) FROM Users WHERE Email = @Email AND UserId != @UserId";
            await using var checkConn = new SqlConnection(connStr);
            await checkConn.OpenAsync();
            await using var checkCmd = new SqlCommand(checkSql, checkConn);
            checkCmd.Parameters.AddWithValue("@Email", email);
            checkCmd.Parameters.AddWithValue("@UserId", userId);
            var exists = (int)(await checkCmd.ExecuteScalarAsync() ?? 0) > 0;
            if (exists)
                return Json(new { success = false, message = "Email is already in use by another user." });

            const string sql = @"
            UPDATE Users
            SET FullName   = @FullName,
                Email      = @Email,
                Department = @Department,
                Role       = @Role,
                UpdatedAt  = @Updated
            WHERE UserId   = @UserId";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Department", (object?)department ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Role", role);
            cmd.Parameters.AddWithValue("@Updated", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@UserId", userId);
            var rows = await cmd.ExecuteNonQueryAsync();

            if (rows == 0)
                return Json(new { success = false, message = "User not found." });

            await WriteAudit(null, "USER_UPDATED", User.Identity!.Name!,
                $"Updated user ID {userId}: email={email}, role={role}, dept={department}");

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", userId);
            return Json(new { success = false, message = "Server error." });
        }
    }

    // ── POST /Admin/ReactivateUser/{userId} ──────────────────────
    [HttpPost("ReactivateUser/{userId}")]
    public async Task<IActionResult> ReactivateUser(int userId)
    {
        try
        {
            var connStr = _config.GetConnectionString("ClaimsDB")!;
            const string sql = "UPDATE Users SET IsActive = 1, UpdatedAt = @Updated WHERE UserId = @UserId";
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Updated", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@UserId", userId);
            var rows = await cmd.ExecuteNonQueryAsync();

            if (rows == 0)
                return Json(new { success = false, message = "User not found." });

            await WriteAudit(null, "USER_REACTIVATED", User.Identity!.Name!,
                $"User ID {userId} has been reactivated.");

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reactivating user {UserId}", userId);
            return Json(new { success = false, message = "Server error." });
        }
    }

    // ── GET /Admin/ExportClaims ───────────────────────────────────
    [HttpGet("ExportClaims")]
    public async Task<IActionResult> ExportClaims(
        string? statusFilter = null,
        string? searchTerm = null)
    {
        try
        {
            var connStr = _config.GetConnectionString("ClaimsDB")!;

            var where = new List<string>();
            var parms = new List<SqlParameter>();

            if (!string.IsNullOrEmpty(statusFilter))
            {
                where.Add("ClaimStatus = @Status");
                parms.Add(new SqlParameter("@Status", statusFilter));
            }
            if (!string.IsNullOrEmpty(searchTerm))
            {
                where.Add("(ClaimId LIKE @S OR MemberId LIKE @S OR ProviderId LIKE @S OR SubmittedBy LIKE @S)");
                parms.Add(new SqlParameter("@S", $"%{searchTerm}%"));
            }

            var whereClause = where.Any() ? "WHERE " + string.Join(" AND ", where) : "";

            var sql = $@"
            SELECT ClaimId, MemberId, ProviderId, DiagnosisCode,
                   ClaimAmount, ClaimStatus, Priority,
                   SubmittedBy, SubmittedAt, ProcessedAt, ProcessedBy
            FROM ClaimHeader
            {whereClause}
            ORDER BY SubmittedAt DESC";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddRange(parms.ToArray());

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ClaimId,MemberId,ProviderId,DiagnosisCode,ClaimAmount,Status,Priority,SubmittedBy,SubmittedAt,ProcessedAt,ProcessedBy");

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(reader.GetString(0)),
                    CsvEscape(reader.GetString(1)),
                    CsvEscape(reader.GetString(2)),
                    CsvEscape(reader.GetString(3)),
                    reader.GetDecimal(4).ToString("F2"),
                    CsvEscape(reader.GetString(5)),
                    CsvEscape(reader.GetString(6)),
                    CsvEscape(reader.GetString(7)),
                    reader.GetDateTime(8).ToString("yyyy-MM-dd HH:mm:ss"),
                    reader.IsDBNull(9) ? "" : reader.GetDateTime(9).ToString("yyyy-MM-dd HH:mm:ss"),
                    reader.IsDBNull(10) ? "" : CsvEscape(reader.GetString(10))
                ));
            }

            var filename = $"claims-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());

            await WriteAudit(null, "CLAIMS_EXPORTED", User.Identity!.Name!,
                $"Exported claims (filter: {statusFilter ?? "all"}, search: {searchTerm ?? "none"})");

            return File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting claims");
            TempData["ErrorMessage"] = "Export failed. Please try again.";
            return RedirectToAction("AllClaims");
        }
    }

    // ── GET /Admin/AuditLog ───────────────────────────────────────
    [HttpGet("AuditLog")]
    public async Task<IActionResult> AuditLog(int page = 1, string? action = null)
    {
        try
        {
            var connStr = _config.GetConnectionString("ClaimsDB")!;
            var pageSize = 50;
            var offset = (page - 1) * pageSize;

            var where = action is not null ? "WHERE Action = @Action" : "";
            var parms = new List<SqlParameter>();
            if (action is not null) parms.Add(new SqlParameter("@Action", action));

            var sql = $@"
            SELECT AuditId, ClaimId, Action, PerformedBy, IPAddress, Details, Timestamp
            FROM AuditTrail {where}
            ORDER BY Timestamp DESC
            OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddRange(parms.ToArray());
            await using var reader = await cmd.ExecuteReaderAsync();

            var logs = new List<AuditLogViewModel>();
            while (await reader.ReadAsync())
            {
                logs.Add(new AuditLogViewModel
                {
                    AuditId = reader.GetInt32(0),
                    ClaimId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Action = reader.GetString(2),
                    PerformedBy = reader.GetString(3),
                    IPAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Details = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Timestamp = reader.GetDateTime(6)
                });
            }

            ViewBag.CurrentPage = page;
            ViewBag.ActionFilter = action;
            return View(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading audit log");
            return View(new List<AuditLogViewModel>());
        }
    }

    // ── Private helpers ───────────────────────────────────────────
    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch { return false; }
    }
}
