using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;

namespace ClaimIntake.Web.Controllers;

public abstract class BaseController : Controller
{
    private readonly IConfiguration _config;

    protected BaseController(IConfiguration config)
    {
        _config = config;
    }

    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            ViewBag.UnreadNotificationCount = await GetUnreadCountAsync();
        }
        await next();
    }

    private async Task<int> GetUnreadCountAsync()
    {
        try
        {
            var connStr = _config.GetConnectionString("ClaimsDB")!;
            var username = User.Identity!.Name!;
            const string sql = @"
                SELECT COUNT(*) FROM Notifications n
                JOIN Users u ON u.UserId = n.UserId
                WHERE u.Username = @Username AND n.IsRead = 0";
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Username", username);
            return (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }
        catch { return 0; }
    }
}