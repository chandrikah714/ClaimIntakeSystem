using Azure.Messaging.ServiceBus;
using ClaimIntake.Domain.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        var encryptionKey = config["EncryptionKey"]
            ?? throw new InvalidOperationException(
                "EncryptionKey missing from local.settings.json!");

        services.AddSingleton(
            _ => new AesEncryptionService(encryptionKey));

        var sbConn = config["ServiceBusConnection"]
            ?? throw new InvalidOperationException(
                "ServiceBusConnection missing from local.settings.json!");

        services.AddSingleton(_ => new ServiceBusClient(sbConn));
    })
    .Build();

await host.RunAsync();
