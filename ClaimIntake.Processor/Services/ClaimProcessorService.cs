// ============================================================
// FILE: ClaimIntake.Processor/Services/ClaimProcessorService.cs
// PURPOSE: The "heart" of the Windows Service.
//          Continuously listens to the Service Bus queue,
//          decrypts messages, validates, and saves to SQL.
//
// BEGINNER ANALOGY: Imagine a mail sorting room.
// This service is the worker who:
// - Stands at the mailbox all day
// - Takes out each sealed envelope (encrypted message)
// - Opens it with the key (decrypts)
// - Checks the paperwork (validates)
// - Files it in the cabinet (saves to database)
// - If the paperwork is bad, puts it in a special tray (dead-letter queue)
// ============================================================

using Azure.Messaging.ServiceBus;
using ClaimIntake.Domain.Models;
using ClaimIntake.Domain.Services;
using ClaimIntake.Domain.Validation;
using ClaimIntake.Processor.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClaimIntake.Processor.Services;

// BackgroundService is the base class for long-running services
// We inherit from it and override ExecuteAsync()
public class ClaimProcessorService : BackgroundService
{
    private readonly ServiceBusClient _sbClient;
    private readonly IEncryptionService _encryption;
    private readonly IClaimRepository _repository;
    private readonly IConfiguration _config;
    private readonly ILogger<ClaimProcessorService> _logger;

    // ServiceBusProcessor is what actually listens to the queue
    private ServiceBusProcessor? _processor;

    // Stats: how many messages we've processed since startup
    private int _successCount = 0;
    private int _failureCount = 0;

    public ClaimProcessorService(
        ServiceBusClient sbClient,
        IEncryptionService encryption,
        IClaimRepository repository,
        IConfiguration config,
        ILogger<ClaimProcessorService> logger)
    {
        _sbClient = sbClient;
        _encryption = encryption;
        _repository = repository;
        _config = config;
        _logger = logger;
    }

    // ── ExecuteAsync: Called once when the service starts ────────────────────
    // ct = CancellationToken: when this is cancelled, we should stop
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var queueName = _config["ClaimQueueName"] ?? "claim-intake-queue";

        _logger.LogInformation(
            "=== Claim Processor Service starting ===");
        _logger.LogInformation(
            "Listening on queue: '{Queue}'", queueName);

        // Configure the queue processor
        _processor = _sbClient.CreateProcessor(queueName,
            new ServiceBusProcessorOptions
            {
                // How many messages to process at the same time (parallel)
                MaxConcurrentCalls = 4,

                // We handle completion MANUALLY (see CompleteMessageAsync below)
                // AutoComplete = false means: only remove from queue after WE say so
                // This is important! If we crash mid-process, the message stays
                // in the queue and gets reprocessed — no data loss!
                AutoCompleteMessages = false,

                // How long to wait for new messages before checking again
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
            });

        // Register our message handler
        _processor.ProcessMessageAsync += HandleMessageAsync;

        // Register our error handler  
        _processor.ProcessErrorAsync += HandleErrorAsync;

        // Start listening!
        await _processor.StartProcessingAsync(ct);
        _logger.LogInformation("Processor is now listening for messages...");

        // Log stats every 60 seconds
        using var statTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (!ct.IsCancellationRequested)
        {
            try { await statTimer.WaitForNextTickAsync(ct); }
            catch (OperationCanceledException) { break; }

            _logger.LogInformation(
                "Processor stats — Success: {S}, Failed: {F}",
                _successCount, _failureCount);
        }
    }

    // ── HandleMessageAsync: Called for EACH message from the queue ────────────
    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        // Get the raw message body (JSON string)
        var rawBody = args.Message.Body.ToString();
        var messageId = args.Message.MessageId;

        _logger.LogInformation(
            "Processing message {MessageId} (delivery #{Count})",
            messageId, args.Message.DeliveryCount);

        EncryptedPayload? envelope = null;
        ClaimDto? claim = null;

        try
        {
            // ── STEP 1: DESERIALIZE the encrypted envelope ────────────────
            // The message body is a JSON-serialized EncryptedPayload
            envelope = JsonSerializer.Deserialize<EncryptedPayload>(rawBody);

            if (envelope == null)
                throw new InvalidOperationException("Message deserialized to null.");

            // ── STEP 2: DECRYPT the claim data ────────────────────────────
            // Using our AES-256 key to unscramble the payload
            claim = _encryption.Decrypt(envelope);

            _logger.LogInformation(
                "Decrypted claim {ClaimId} for member {MemberId}",
                claim.ClaimId, claim.MemberId);

            // ── STEP 3: VALIDATE again (defence in depth) ─────────────────
            // We validated in the Azure Function too, but we validate again here.
            // Why? The message could theoretically come from another source.
            // "Trust but verify" — or better: "Never trust, always verify"
            var (isValid, validationErrors) = ClaimValidator.Validate(claim);

            if (!isValid)
            {
                _logger.LogWarning(
                    "Claim {ClaimId} failed processor validation: {Errors}",
                    claim.ClaimId, string.Join("; ", validationErrors));

                // Dead-letter: move to a special "failed" sub-queue
                // Dead-lettered messages don't get retried automatically.
                // An admin can inspect them in Service Bus Explorer.
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "ValidationFailed",
                    deadLetterErrorDescription: string.Join("; ", validationErrors));

                Interlocked.Increment(ref _failureCount);
                return;  // Don't process further
            }

            // ── STEP 4: APPLY BUSINESS RULES ─────────────────────────────
            // Add more rules here as your domain grows:
            // - Check for duplicate claims
            // - Verify member is active
            // - Check provider is in-network
            // - Apply coverage limits
            // For now, we just check amount thresholds as example:
            if (claim.ClaimAmount > 50_000m)
            {
                _logger.LogWarning(
                    "Claim {ClaimId} flagged for manual review (amount: {Amount})",
                    claim.ClaimId, claim.ClaimAmount);
                // In real system: route to a different queue or table for review
                // For now, we'll still save it but flag it
            }

            // ── STEP 5: SAVE TO DATABASE ──────────────────────────────────
            await _repository.SaveClaimAsync(claim);

            _logger.LogInformation(
                "Claim {ClaimId} saved to database. Amount: ${Amount}",
                claim.ClaimId, claim.ClaimAmount);

            // ── STEP 6: COMPLETE the message ──────────────────────────────
            // This tells Service Bus: "I handled this successfully, remove it from queue"
            // If we DON'T call this, the message stays and gets redelivered after timeout
            await args.CompleteMessageAsync(args.Message);

            Interlocked.Increment(ref _successCount);

            _logger.LogInformation(
                "Message {MessageId} completed successfully.", messageId);
        }
        catch (JsonException ex)
        {
            // Message is malformed JSON — will never succeed, dead-letter it
            _logger.LogError(ex,
                "Malformed JSON in message {MessageId}. Dead-lettering.", messageId);
            await args.DeadLetterMessageAsync(args.Message,
                "MalformedJson", ex.Message);
            Interlocked.Increment(ref _failureCount);
        }
        catch (CryptographicException ex)
        {
            // Decryption failed — wrong key, corrupted data, etc.
            _logger.LogError(ex,
                "Decryption failed for message {MessageId}. Dead-lettering.", messageId);
            await args.DeadLetterMessageAsync(args.Message,
                "DecryptionFailed", ex.Message);
            Interlocked.Increment(ref _failureCount);
        }
        catch (Exception ex)
        {
            // Any other error: ABANDON the message
            // Abandon = put it back in queue for retry
            // After MaxDeliveryCount retries, Service Bus auto-dead-letters it
            _logger.LogError(ex,
                "Unhandled error processing message {MessageId}. Abandoning.", messageId);

            await args.AbandonMessageAsync(args.Message,
                new Dictionary<string, object>
                {
                    ["LastError"] = ex.Message,
                    ["LastAttemptAt"] = DateTime.UtcNow.ToString("O")
                });
            Interlocked.Increment(ref _failureCount);
        }
    }

    // ── HandleErrorAsync: Called when the processor itself has an error ──────
    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus processor error. Source: {Source}, Entity: {Entity}",
            args.ErrorSource, args.EntityPath);

        // Return completed task — the processor will handle recovery automatically
        return Task.CompletedTask;
    }

    // ── StopAsync: Called when the service is stopping ───────────────────────
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Claim Processor Service stopping...");

        if (_processor != null)
        {
            // Gracefully stop: finish current messages, don't accept new ones
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        _logger.LogInformation(
            "=== Processor stopped. Final stats — Success: {S}, Failed: {F} ===",
            _successCount, _failureCount);

        await base.StopAsync(cancellationToken);
    }
}