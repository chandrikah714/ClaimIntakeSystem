// ============================================================
// FILE: ClaimIntake.API/Functions/ClaimIntakeFunction.cs
// PURPOSE: The Azure Function that receives claim form data,
//          validates it, encrypts it, then sends it to
//          Azure Service Bus for async processing.
//
// BEGINNER: Think of this as a POST OFFICE WINDOW.
// 1. You (the web app) walk up and hand over a claim form
// 2. The clerk (this function) checks it's filled correctly
// 3. Puts it in a sealed envelope (encrypts it)
// 4. Drops it in the outbox (Service Bus queue)
// 5. Gives you a receipt (202 Accepted response)
// The actual processing happens LATER by someone else (the Windows Service)
// ============================================================

using Azure.Messaging.ServiceBus;
using ClaimIntake.Domain.Models;
using ClaimIntake.Domain.Services;
using ClaimIntake.Domain.Validation;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace ClaimIntake.API.Functions;

public class ClaimIntakeFunction
{
    // These are injected via the constructor (Dependency Injection)
    private readonly IEncryptionService _encryption;
    private readonly ServiceBusClient _sbClient;
    private readonly IConfiguration _config;
    private readonly ILogger<ClaimIntakeFunction> _logger;

    public ClaimIntakeFunction(
        IEncryptionService encryption,
        ServiceBusClient sbClient,
        IConfiguration config,
        ILogger<ClaimIntakeFunction> logger)
    {
        _encryption = encryption;
        _sbClient = sbClient;
        _config = config;
        _logger = logger;
    }

    // ── THE FUNCTION TRIGGER ─────────────────────────────────────────────────
    // [Function("SubmitClaim")]  → the function name shown in Azure Portal
    // [HttpTrigger(...)]         → triggered by HTTP POST requests
    // Route = "claims"           → URL is: /api/claims
    // AuthorizationLevel.Function → caller must pass a "x-functions-key" header
    [Function("SubmitClaim")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "claims")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ClaimIntakeFunction triggered at {Time}", DateTime.UtcNow);

        // ── STEP 1: READ THE REQUEST BODY ────────────────────────────────────
        // The web app sent JSON in the request body; we read it here
        string requestBody;
        try
        {
            requestBody = await req.ReadAsStringAsync() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read request body.");
            return await BuildResponse(req, HttpStatusCode.BadRequest,
                "Could not read request body.");
        }

        if (string.IsNullOrWhiteSpace(requestBody))
            return await BuildResponse(req, HttpStatusCode.BadRequest,
                "Request body is empty. Please send claim data as JSON.");

        // ── STEP 2: DESERIALIZE JSON → ClaimDto OBJECT ───────────────────────
        // JsonSerializer.Deserialize converts a JSON string into a C# object
        ClaimDto? claim;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true  // Accept camelCase and PascalCase
            };
            claim = JsonSerializer.Deserialize<ClaimDto>(requestBody, options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Invalid JSON in request: {Error}", ex.Message);
            return await BuildResponse(req, HttpStatusCode.BadRequest,
                $"Invalid JSON format: {ex.Message}");
        }

        if (claim == null)
            return await BuildResponse(req, HttpStatusCode.BadRequest,
                "Claim data could not be parsed.");

        // Assign a new unique ClaimId if not provided
        if (string.IsNullOrWhiteSpace(claim.ClaimId))
            claim.ClaimId = Guid.NewGuid().ToString();

        _logger.LogInformation("Processing claim {ClaimId} from {User}",
            claim.ClaimId, claim.SubmittedBy);

        // ── STEP 3: VALIDATE THE CLAIM ───────────────────────────────────────
        // Run all our business rules (required fields, ICD-10 format, amount range)
        var (isValid, validationErrors) = ClaimValidator.Validate(claim);

        if (!isValid)
        {
            _logger.LogWarning("Claim {ClaimId} failed validation: {Errors}",
                claim.ClaimId, string.Join("; ", validationErrors));

            // Return 422 Unprocessable Entity with the list of errors
            return await BuildResponse(req, HttpStatusCode.UnprocessableContent,
                "Claim validation failed.",
                new { errors = validationErrors, claimId = claim.ClaimId });
        }

        // ── STEP 4: ENCRYPT THE CLAIM PAYLOAD ───────────────────────────────
        // We encrypt BEFORE putting it on the queue.
        // Even if someone intercepts the queue message, they can't read it!
        EncryptedPayload encryptedPayload;
        try
        {
            encryptedPayload = _encryption.Encrypt(claim);
            _logger.LogInformation("Claim {ClaimId} encrypted successfully (KeyId: {KeyId})",
                claim.ClaimId, encryptedPayload.KeyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encryption failed for claim {ClaimId}", claim.ClaimId);
            return await BuildResponse(req, HttpStatusCode.InternalServerError,
                "Encryption error. Please contact support.");
        }

        // ── STEP 5: SEND TO AZURE SERVICE BUS QUEUE ──────────────────────────
        // Serialize the encrypted envelope to JSON and send it to the queue
        var queueName = _config["ClaimQueueName"] ?? "claim-intake-queue";

        try
        {
            var sender = _sbClient.CreateSender(queueName);
            var messageBody = JsonSerializer.Serialize(encryptedPayload);

            // ServiceBusMessage is the "envelope" we put on the queue
            var sbMessage = new ServiceBusMessage(messageBody)
            {
                ContentType = "application/json",
                MessageId = claim.ClaimId,       // Helps detect duplicates
                Subject = "ClaimIntake",
                CorrelationId = claim.SubmittedBy,   // Track who submitted

                // Message expires after 7 days if not processed
                TimeToLive = TimeSpan.FromDays(7)
            };

            // Add custom properties we can inspect in Service Bus Explorer
            sbMessage.ApplicationProperties["ClaimId"] = claim.ClaimId;
            sbMessage.ApplicationProperties["MemberId"] = claim.MemberId;
            sbMessage.ApplicationProperties["SubmittedBy"] = claim.SubmittedBy;
            sbMessage.ApplicationProperties["Amount"] = (double)claim.ClaimAmount;

            await sender.SendMessageAsync(sbMessage, cancellationToken);

            _logger.LogInformation(
                "Claim {ClaimId} queued successfully on '{Queue}'",
                claim.ClaimId, queueName);
        }
        catch (ServiceBusException ex) when (ex.IsTransient)
        {
            // Transient errors are temporary (network blip, service busy)
            // Log and return 503 so the client can retry
            _logger.LogError(ex,
                "Transient Service Bus error for claim {ClaimId}", claim.ClaimId);
            return await BuildResponse(req, HttpStatusCode.ServiceUnavailable,
                "Messaging service temporarily unavailable. Please retry.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send claim {ClaimId} to Service Bus", claim.ClaimId);
            return await BuildResponse(req, HttpStatusCode.InternalServerError,
                "Failed to queue claim. Please try again.");
        }

        // ── STEP 6: RETURN SUCCESS RESPONSE ─────────────────────────────────
        // 202 Accepted means: "I got it, it's being processed asynchronously"
        // (Different from 200 OK which means "fully done right now")
        return await BuildResponse(req, HttpStatusCode.Accepted,
            $"Claim {claim.ClaimId} accepted and queued for processing.",
            new
            {
                claimId = claim.ClaimId,
                status = "Queued",
                submittedAt = claim.SubmittedAt,
                message = "Your claim is being processed. Keep your Claim ID for reference."
            });
    }

    // ── HELPER: Build an HTTP response with JSON body ────────────────────────
    private static async Task<HttpResponseData> BuildResponse(
        HttpRequestData req,
        HttpStatusCode statusCode,
        string message,
        object? data = null)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var payload = new
        {
            message,
            statusCode = (int)statusCode,
            timestamp = DateTime.UtcNow,
            data
        };

        var json = JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = false });

        await response.WriteStringAsync(json);
        return response;
    }
}
