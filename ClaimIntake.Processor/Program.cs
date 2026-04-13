// ============================================================
// FILE: ClaimIntake.Processor/Program.cs
// PURPOSE: Entry point for the Windows Service.
//          Sets up everything the processor needs and starts it.
//
// BEGINNER: A Windows Service is a background program that:
// - Starts automatically when Windows boots
// - Runs with no visible window
// - Keeps running 24/7 even when no one is logged in
// - Perfect for processing queue messages continuously!
// ============================================================

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using ClaimIntake.Domain.Services;
using ClaimIntake.Processor.Repositories;
using ClaimIntake.Processor.Services;

// IHostBuilder sets up the whole runtime environment
var host = Host.CreateDefaultBuilder(args)

    // .UseWindowsService() makes this a real Windows Service
    // Without this, it's just a console app
    .UseWindowsService(options =>
    {
        options.ServiceName = "ClaimIntakeProcessor";  // Name shown in services.msc
    })

    // Configure Logging: write to Windows Event Log AND console
    .ConfigureLogging((ctx, logging) =>
    {
        logging.AddEventLog(settings =>
        {
            settings.SourceName = "ClaimIntakeProcessor";  // Event Log source name
        });
        logging.AddConsole();  // Also write to console (useful during testing)
    })

    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // ── Encryption Service ───────────────────────────────────────
        var encryptionKey = config["EncryptionKey"]
            ?? throw new InvalidOperationException(
                "EncryptionKey is not configured! " +
                "Set it in appsettings.json or environment variable.");

        services.AddSingleton<IEncryptionService>(
            _ => new AesEncryptionService(encryptionKey));

        // ── Azure Service Bus Client ─────────────────────────────────
        var sbConn = config["ServiceBusConnection"]
            ?? throw new InvalidOperationException(
                "ServiceBusConnection is not configured!");

        // Prepare options once
        var sbOptions = new ServiceBusClientOptions
        {
            // Retry up to 3 times with exponential backoff
            RetryOptions = new ServiceBusRetryOptions
            {
                MaxRetries = 3,
                Mode = ServiceBusRetryMode.Exponential,
                MaxDelay = TimeSpan.FromSeconds(30)
            }
        };

        if (sbConn.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase) ||
            sbConn.Contains("SharedAccessKey", StringComparison.OrdinalIgnoreCase))
        {
            // Value looks like a connection string
            services.AddSingleton(_ => new ServiceBusClient(sbConn, sbOptions));
        }
        else
        {
            // Value looks like a fully-qualified namespace — use a credential (e.g. DefaultAzureCredential)
            services.AddSingleton(_ => new ServiceBusClient(sbConn, new DefaultAzureCredential(), sbOptions));
        }

        // ── Claim Repository (talks to SQL Server) ───────────────────
        services.AddSingleton<IClaimRepository, SqlClaimRepository>();

        // ── The main background worker ───────────────────────────────
        // AddHostedService registers a class that runs continuously
        services.AddHostedService<ClaimProcessorService>();
    })
    .Build();

// Start the service — it will run forever until stopped
await host.RunAsync();