using Azure.Messaging.ServiceBus;
using ClaimIntake.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        var key = config["EncryptionKey"]
            ?? throw new InvalidOperationException(
                "EncryptionKey missing from local.settings.json!");

        services.AddSingleton<IEncryptionService>(
            _ => new AesEncryptionService(key));

        var sb = config["ServiceBusConnection"]
            ?? throw new InvalidOperationException(
                "ServiceBusConnection missing from local.settings.json!");

        services.AddSingleton(_ => new ServiceBusClient(sb));
    })
    .Build();

await host.RunAsync();