// ============================================================
// FILE: ClaimIntake.Web/Controllers/NotificationsController.cs
// PURPOSE: API endpoints for the notification system
// ============================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClaimIntake.Web.Models;

namespace ClaimIntake.Web.Controllers;

[Authorize]
[Route("Notifications")]
public class NotificationsController : BaseController
{
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(IConfiguration config, ILogger<NotificationsController> logger) : base(config)
    {
        _config = config;
        _logger = logger;
    }

    // GET /Notifications/Recent — called by navbar JS
    [HttpGet("Recent")]
    public async Task<IActionResult> Recent(int count = 8)
    {
        try
        {
            var userId = await GetCurrentUserId();
            var notifs = await GetRecentNotificationsAsync(userId, count);
            return Json(notifs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading notifications");
            return Json(new List<object>());
        }
    }

    // GET /Notifications — full page
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        try
        {
            var userId = await GetCurrentUserId();
            var notifs = await GetAllNotificationsAsync(userId);
            return View(notifs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading all notifications");
            return View(new List<NotificationViewModel>());
        }
    }

    // POST /Notifications/MarkRead/{id}
    [HttpPost("MarkRead/{id:int}")]
    public async Task<IActionResult> MarkRead(int id)
    {
        try
        {
            var userId = await GetCurrentUserId();
            var sql = "UPDATE Notifications SET IsRead=1, ReadAt=GETUTCDATE() WHERE NotificationId=@Id AND UserId=@UserId";
            await ExecuteNonQueryAsync(sql,
                new SqlParameter("@Id", id),
                new SqlParameter("@UserId", userId));

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification read");
            return Json(new { success = false });
        }
    }

    // POST /Notifications/MarkAllRead
    [HttpPost("MarkAllRead")]
    public async Task<IActionResult> MarkAllRead()
    {
        try
        {
            var userId = await GetCurrentUserId();
            var sql = "UPDATE Notifications SET IsRead=1, ReadAt=GETUTCDATE() WHERE UserId=@UserId AND IsRead=0";
            await ExecuteNonQueryAsync(sql, new SqlParameter("@UserId", userId));
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications read");
            return Json(new { success = false });
        }
    }

    // POST /Notifications/Delete/{id}
    [HttpPost("Delete/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var userId = await GetCurrentUserId();
            var sql = "DELETE FROM Notifications WHERE NotificationId=@Id AND UserId=@UserId";
            await ExecuteNonQueryAsync(sql,
                new SqlParameter("@Id", id),
                new SqlParameter("@UserId", userId));
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification");
            return Json(new { success = false });
        }
    }

    // GET /Notifications/UnreadCount — for polling
    [HttpGet("UnreadCount")]
    public async Task<IActionResult> UnreadCount()
    {
        try
        {
            var userId = await GetCurrentUserId();
            var count = await GetUnreadCountAsync(userId);
            return Json(new { count });
        }
        catch
        {
            return Json(new { count = 0 });
        }
    }

    // ── Helpers ─────────────────────────────────────────────

    private async Task<int> GetCurrentUserId()
    {
        var username = User.Identity!.Name!;
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        const string sql = "SELECT UserId FROM Users WHERE Username=@Username";
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Username", username);
        return (int)(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private async Task<int> GetUnreadCountAsync(int userId)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        const string sql = "SELECT COUNT(*) FROM Notifications WHERE UserId=@UserId AND IsRead=0";
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);
        return (int)(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private async Task<List<NotificationViewModel>> GetRecentNotificationsAsync(int userId, int count)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        var sql = $@"
            SELECT TOP {count}
                NotificationId, Title, Message, Type, IsRead, ClaimId, ActionUrl, CreatedAt
            FROM Notifications
            WHERE UserId = @UserId
            ORDER BY CreatedAt DESC";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);

        var list = new List<NotificationViewModel>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new NotificationViewModel
            {
                NotificationId = reader.GetInt32(0),
                Title = reader.GetString(1),
                Message = reader.GetString(2),
                Type = reader.GetString(3),
                IsRead = reader.GetBoolean(4),
                ClaimId = reader.IsDBNull(5) ? null : reader.GetString(5),
                ActionUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            });
        }
        return list;
    }

    private async Task<List<NotificationViewModel>> GetAllNotificationsAsync(int userId)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        const string sql = @"
            SELECT NotificationId, Title, Message, Type, IsRead, ClaimId, ActionUrl, CreatedAt
            FROM Notifications
            WHERE UserId = @UserId
            ORDER BY IsRead ASC, CreatedAt DESC";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);

        var list = new List<NotificationViewModel>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new NotificationViewModel
            {
                NotificationId = reader.GetInt32(0),
                Title = reader.GetString(1),
                Message = reader.GetString(2),
                Type = reader.GetString(3),
                IsRead = reader.GetBoolean(4),
                ClaimId = reader.IsDBNull(5) ? null : reader.GetString(5),
                ActionUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7)
            });
        }
        return list;
    }

    private async Task ExecuteNonQueryAsync(string sql, params SqlParameter[] parameters)
    {
        var connStr = _config.GetConnectionString("ClaimsDB")!;
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters);
        await cmd.ExecuteNonQueryAsync();
    }
}
