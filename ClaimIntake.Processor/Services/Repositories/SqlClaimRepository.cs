// ============================================================
// FILE: ClaimIntake.Processor/Repositories/SqlClaimRepository.cs
// PURPOSE: Handles all database operations for claims.
//
// PATTERN: Repository Pattern
// Instead of writing SQL scattered everywhere in your code,
// you put ALL database code in one place: the Repository.
// The rest of your code just calls methods like SaveClaimAsync()
// and doesn't need to know HOW it works internally.
//
// BEGINNER SQL TIPS:
// - parameterized queries (using @ParameterName) are SAFE
// - string concatenation in SQL is DANGEROUS (SQL injection!)
// - Always use using statements to close connections
// ============================================================

using ClaimIntake.Domain.Models;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace ClaimIntake.Processor.Repositories;

// Interface: defines WHAT the repository can do (not HOW)
public interface IClaimRepository
{
    Task SaveClaimAsync(ClaimDto claim);
    Task<bool> ClaimExistsAsync(string claimId);
    Task UpdateClaimStatusAsync(string claimId, string status, string changedBy);
}

public class SqlClaimRepository : IClaimRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqlClaimRepository> _logger;

    // Polly retry policy: if the DB save fails, retry up to 3 times
    // with increasing waits: 2s, 4s, 8s (exponential backoff)
    private readonly AsyncRetryPolicy _retryPolicy;

    public SqlClaimRepository(
        IConfiguration config,
        ILogger<SqlClaimRepository> logger)
    {
        _connectionString = config.GetConnectionString("ClaimsDB")
            ?? throw new InvalidOperationException("ClaimsDB connection string not found!");

        _logger = logger;

        // Define the retry policy
        _retryPolicy = Policy
            .Handle<SqlException>(ex => IsTransientSqlError(ex))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2s, 4s, 8s
                onRetry: (exception, timespan, attempt, _) =>
                {
                    _logger.LogWarning(
                        "SQL retry {Attempt} after {Delay}s. Error: {Error}",
                        attempt, timespan.TotalSeconds, exception.Message);
                });
    }

    // ── SaveClaimAsync ────────────────────────────────────────────────────────
    public async Task SaveClaimAsync(ClaimDto claim)
    {
        // Check for duplicate: don't save the same claim twice!
        // (A message could be delivered twice if there's a network hiccup)
        if (await ClaimExistsAsync(claim.ClaimId))
        {
            _logger.LogWarning(
                "Claim {ClaimId} already exists in DB. Skipping duplicate.",
                claim.ClaimId);
            return;  // Idempotent: safe to call multiple times
        }

        // Execute with retry policy
        await _retryPolicy.ExecuteAsync(async () =>
        {
            // 'await using' = async version of 'using' (ensures connection is closed)
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Use a transaction: either BOTH inserts succeed, or NEITHER does
            // This prevents having a claim with no audit record (or vice versa)
            await using var transaction = conn.BeginTransaction();

            try
            {
                // ── INSERT INTO ClaimHeader ───────────────────────────────
                const string insertClaim = @"
                    INSERT INTO ClaimHeader
                        (ClaimId, MemberId, ProviderId, DiagnosisCode,
                         ClaimAmount, SubmittedBy, SubmittedAt, 
                         ProcessedAt, ClaimStatus)
                    VALUES
                        (@ClaimId, @MemberId, @ProviderId, @DiagnosisCode,
                         @ClaimAmount, @SubmittedBy, @SubmittedAt,
                         @ProcessedAt, 'Received')";

                await using var claimCmd = new SqlCommand(insertClaim, conn, transaction);
                // Always use parameters — NEVER concatenate user data into SQL!
                claimCmd.Parameters.AddWithValue("@ClaimId", claim.ClaimId);
                claimCmd.Parameters.AddWithValue("@MemberId", claim.MemberId);
                claimCmd.Parameters.AddWithValue("@ProviderId", claim.ProviderId);
                claimCmd.Parameters.AddWithValue("@DiagnosisCode", claim.DiagnosisCode);
                claimCmd.Parameters.AddWithValue("@ClaimAmount", claim.ClaimAmount);
                claimCmd.Parameters.AddWithValue("@SubmittedBy", claim.SubmittedBy);
                claimCmd.Parameters.AddWithValue("@SubmittedAt", claim.SubmittedAt);
                claimCmd.Parameters.AddWithValue("@ProcessedAt", DateTime.UtcNow);
                await claimCmd.ExecuteNonQueryAsync();

                // ── INSERT INTO ClaimStatusHistory ────────────────────────
                const string insertHistory = @"
                    INSERT INTO ClaimStatusHistory
                        (ClaimId, Status, ChangedBy, Notes)
                    VALUES
                        (@ClaimId, 'Received', 'ProcessorService',
                         'Claim decrypted, validated, and saved by processor')";

                await using var histCmd = new SqlCommand(insertHistory, conn, transaction);
                histCmd.Parameters.AddWithValue("@ClaimId", claim.ClaimId);
                await histCmd.ExecuteNonQueryAsync();

                // ── INSERT INTO AuditTrail ────────────────────────────────
                const string insertAudit = @"
                    INSERT INTO AuditTrail
                        (ClaimId, Action, PerformedBy, Details)
                    VALUES
                        (@ClaimId, 'CLAIM_PROCESSED', @PerformedBy, @Details)";

                await using var auditCmd = new SqlCommand(insertAudit, conn, transaction);
                auditCmd.Parameters.AddWithValue("@ClaimId", claim.ClaimId);
                auditCmd.Parameters.AddWithValue("@PerformedBy", claim.SubmittedBy);
                auditCmd.Parameters.AddWithValue("@Details",
                    $"Claim processed. Member: {claim.MemberId}, " +
                    $"Provider: {claim.ProviderId}, Amount: {claim.ClaimAmount:C}");
                await auditCmd.ExecuteNonQueryAsync();

                // ALL three inserts succeeded — commit the transaction
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Claim {ClaimId} saved to DB with audit trail.", claim.ClaimId);
            }
            catch
            {
                // Something failed — roll back ALL changes (nothing gets saved)
                await transaction.RollbackAsync();
                throw;  // Re-throw so the retry policy can retry
            }
        });
    }

    // ── ClaimExistsAsync: Check if a claim is already in the database ─────────
    public async Task<bool> ClaimExistsAsync(string claimId)
    {
        const string sql =
            "SELECT COUNT(1) FROM ClaimHeader WHERE ClaimId = @ClaimId";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ClaimId", claimId);

        // ExecuteScalarAsync returns the first column of the first row
        // COUNT(1) returns a number: 0 = not found, 1 = found
        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        return count > 0;
    }

    // ── UpdateClaimStatusAsync: Change a claim's status ──────────────────────
    public async Task UpdateClaimStatusAsync(
        string claimId, string status, string changedBy)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = conn.BeginTransaction();

        try
        {
            // Update the main claim record
            const string updateClaim =
                "UPDATE ClaimHeader SET ClaimStatus = @Status WHERE ClaimId = @ClaimId";
            await using var updateCmd = new SqlCommand(updateClaim, conn, transaction);
            updateCmd.Parameters.AddWithValue("@Status", status);
            updateCmd.Parameters.AddWithValue("@ClaimId", claimId);
            await updateCmd.ExecuteNonQueryAsync();

            // Log the status change in history
            const string insertHistory = @"
                INSERT INTO ClaimStatusHistory (ClaimId, Status, ChangedBy)
                VALUES (@ClaimId, @Status, @ChangedBy)";
            await using var histCmd = new SqlCommand(insertHistory, conn, transaction);
            histCmd.Parameters.AddWithValue("@ClaimId", claimId);
            histCmd.Parameters.AddWithValue("@Status", status);
            histCmd.Parameters.AddWithValue("@ChangedBy", changedBy);
            await histCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // ── Helper: Is this SQL error temporary? (worth retrying?) ───────────────
    // Some SQL errors are permanent (wrong table name, bad data types)
    // Some are temporary (server busy, network timeout) — worth retrying
    private static bool IsTransientSqlError(SqlException ex)
    {
        // SQL error numbers that are transient/temporary:
        var transientErrors = new HashSet<int>
        {
            -2,     // Timeout
            20,     // The instance of SQL Server does not support encryption
            64,     // A connection was successfully established but then an error
            233,    // No process is on the other end of the pipe
            10053,  // Transport-level error
            10054,  // Connection reset by peer
            10060,  // A connection attempt failed
            40197,  // The service has encountered an error processing your request
            40501,  // The service is currently busy
            40613,  // Database is not currently available
            49918,  // Cannot process request
            4060,   // Cannot open database
            4221,   // Login to read-secondary failed
        };

        return transientErrors.Contains(ex.Number);
    }
}