using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using PoolCar.DbSetup;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<DbSetupOptions>().BindConfiguration(DbSetupOptions.Section);
builder.Services.AddSingleton<IValidateOptions<DbSetupOptions>, DbSetupOptionsValidator>();

// The cloud role name comes from OTEL_SERVICE_NAME.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Logging.AddOpenTelemetry(logging => logging.IncludeFormattedMessage = true);
    builder.Services.AddOpenTelemetry().UseAzureMonitorExporter();
}

using var host = builder.Build();
await host.StartAsync();
var logger = host.Services.GetRequiredService<ILogger<DatabaseSetup>>();
var exitCode = 0;

try
{
    var options = host.Services.GetRequiredService<IOptions<DbSetupOptions>>().Value;
    var setup = new DatabaseSetup(
        EntraDataSources.Create(options, new DefaultAzureCredential()),
        new EntraPrincipals(),
        TimeProvider.System,
        logger);
    await setup.RunAsync(options, CancellationToken.None);
}
#pragma warning disable CA1031 // Any failure must end the job with a non-zero exit code.
catch (Exception exception)
#pragma warning restore CA1031
{
    logger.LogError(exception, "Database setup failed.");
    exitCode = 1;
}

await host.StopAsync();
return exitCode;
