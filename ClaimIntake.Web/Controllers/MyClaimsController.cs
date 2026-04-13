// ============================================================
// FILE: ClaimIntake.Web/Controllers/MyClaimsController.cs (FIXED)
// PURPOSE: User claim management with correct routing & view paths
// ============================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaimIntake.Web.Models;

namespace ClaimIntake.Web.Controllers;

[Authorize]  // Must be logged in
[Route("MyAccount")]
public class MyClaimsController : Controller
{
    private readonly IConfiguration _config;
    private readonly ILogger<MyClaimsController> _logger;

    public MyClaimsController(IConfiguration config, ILogger<MyClaimsController> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ── GET /MyAccount/MyClaims ──────────────────────────────────────────────
    [HttpGet("MyClaims")]
    [HttpGet("")]  // Allow /MyAccount/ to route here too
    public async Task<IActionResult> MyClaims(
        string? statusFilter = null,
        int page = 1,
        int pageSize = 10)
    {
        try
        {
            var username = User.Identity!.Name!;
            var claims = await GetUserClaims(username, statusFilter, page, pageSize);
            // View is at Views/MyAccount/MyClaims.cshtml
            return View("MyClaims", claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading user claims");
            TempData["ErrorMessage"] = "Error loading your claims.";
            return View("MyClaims", new UserClaimsViewModel());
        }
    }

    // ── GET /MyAccount/ClaimDetail/{claimId} ─────────────────────────────────
    [HttpGet("ClaimDetail/{claimId}")]
    public async Task<IActionResult> ClaimDetail(string claimId)
    {
        try
        {
            var username = User.Identity!.Name!;
            var claim = await GetUserClaimDetailAsync(claimId, username);

            if (claim == null)
            {
                _logger.LogWarning("User {User} attempted to access claim {ClaimId} they don't own",
                    username, claimId);
                return Forbid();  // 403 Forbidden
            }

            // View is at Views/MyAccount/ClaimDetail.cshtml
            return View("ClaimDetail", claim);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading claim detail");
            TempData["ErrorMessage"] = "Error loading claim details.";
            return RedirectToAction("MyClaims");
        }
    }

    // ── GET /MyAccount/Profile ────────────────────────────────────────────────
    [HttpGet("Profile")]
    public async Task<IActionResult> Profile()
    {
        try
        {
            var username = User.Identity!.Name!;
            var profile = await GetUserProfileAsync(username);
            // View is at Views/MyAccount/Profile.cshtml
            return View("Profile", profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading user profile");
            TempData["ErrorMessage"] = "Error loading profile.";
            return RedirectToAction("MyClaims");
        }
    }

    // ── PRIVATE HELPERS ──────────────────────────────────────────────────────

    private async Task<UserClaimsViewModel> GetUserClaims(
        string username, string? statusFilter, int page, int pageSize)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        var claims = new List<ClaimSummaryViewModel>();

        var whereClauses = new List<string> { "SubmittedBy = @Username" };
        var parameters = new List<SqlParameter>
        {
            new("@Username", username)
        };

        if (!string.IsNullOrEmpty(statusFilter))
        {
            whereClauses.Add("ClaimStatus = @Status");
            parameters.Add(new SqlParameter("@Status", statusFilter));
        }

        var whereClause = string.Join(" AND ", whereClauses);

        var sql = $@"
            SELECT ClaimId, MemberId, ProviderId, ClaimAmount, ClaimStatus, SubmittedAt, SubmittedBy
            FROM ClaimHeader
            WHERE {whereClause}
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

        // Get total count for pagination
        var countSql = $"SELECT COUNT(*) FROM ClaimHeader WHERE {whereClause}";
        await using var countConn = new SqlConnection(connStr);
        await countConn.OpenAsync();
        await using var countCmd = new SqlCommand(countSql, countConn);
        // create fresh SqlParameter instances for countCmd
        foreach (var p in parameters.Where(p => p.ParameterName != "@Offset" && p.ParameterName != "@PageSize"))
        {
            countCmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value ?? DBNull.Value)
            {
                SqlDbType = p.SqlDbType,
                Size = p.Size
                // copy other properties if relevant (Precision, Scale, Direction, etc.)
            });
        }

        var totalCount = (int)(await countCmd.ExecuteScalarAsync() ?? 0);

        return new UserClaimsViewModel
        {
            Claims = claims,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            StatusFilter = statusFilter
        };
    }

    private async Task<UserClaimDetailViewModel?> GetUserClaimDetailAsync(
        string claimId, string username)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;

        const string sql = @"
            SELECT ClaimId, MemberId, ProviderId, DiagnosisCode, ClaimAmount,
                   ClaimStatus, SubmittedAt, SubmittedBy, ProcessedAt
            FROM ClaimHeader
            WHERE ClaimId = @ClaimId AND SubmittedBy = @Username";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ClaimId", claimId);
        cmd.Parameters.AddWithValue("@Username", username);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var claim = new UserClaimDetailViewModel
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

        // Get status history
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

    private async Task<UserProfileViewModel> GetUserProfileAsync(string username)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;

        // Get user info
        const string userSql = @"
            SELECT UserId, Username, Role, IsActive, CreatedAt
            FROM Users
            WHERE Username = @Username";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(userSql, conn);
        cmd.Parameters.AddWithValue("@Username", username);

        var profile = new UserProfileViewModel { Username = username };

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            profile.UserId = reader.GetInt32(0);
            profile.Role = reader.GetString(2);
            profile.CreatedAt = reader.GetDateTime(4);
        }

        // Get claim statistics for this user
        const string statsSql = @"
            SELECT
                COUNT(*) as TotalClaims,
                SUM(CASE WHEN ClaimStatus = 'Pending' THEN 1 ELSE 0 END) as PendingClaims,
                SUM(CASE WHEN ClaimStatus = 'Approved' THEN 1 ELSE 0 END) as ApprovedClaims,
                SUM(CASE WHEN ClaimStatus = 'Rejected' THEN 1 ELSE 0 END) as RejectedClaims,
                SUM(CAST(ClaimAmount as DECIMAL(18,2))) as TotalAmount
            FROM ClaimHeader
            WHERE SubmittedBy = @Username";

        await using var statsCmd = new SqlCommand(statsSql, conn);
        statsCmd.Parameters.AddWithValue("@Username", username);
        await using var statsReader = await statsCmd.ExecuteReaderAsync();

        if (await statsReader.ReadAsync())
        {
            profile.TotalClaimsSubmitted = statsReader.GetInt32(0);
            profile.PendingClaimsCount = statsReader.IsDBNull(1) ? 0 : statsReader.GetInt32(1);
            profile.ApprovedClaimsCount = statsReader.IsDBNull(2) ? 0 : statsReader.GetInt32(2);
            profile.RejectedClaimsCount = statsReader.IsDBNull(3) ? 0 : statsReader.GetInt32(3);
            profile.TotalClaimsAmount = statsReader.IsDBNull(4) ? 0 : statsReader.GetDecimal(4);
        }

        return profile;
    }
}