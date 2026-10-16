using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace PoolCar.ServiceDefaults.Tests;

public class CloudRoleNameTests
{
    [Fact]
    public void Service_name_from_OTEL_SERVICE_NAME_wins_over_the_container_app_name()
    {
        var previous = Environment.GetEnvironmentVariable("CONTAINER_APP_NAME");
        Environment.SetEnvironmentVariable("CONTAINER_APP_NAME", "ca-live-web");
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration["OTEL_SERVICE_NAME"] = "live-web";
            builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
                "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost/";
            builder.AddServiceDefaults();

            using var host = builder.Build();
            var resource = host.Services.GetRequiredService<TracerProvider>().GetResource();

            resource.Attributes.ShouldContain(new KeyValuePair<string, object>("service.name", "live-web"));
            resource.Attributes.ShouldContain(new KeyValuePair<string, object>("cloud.platform", "azure_container_apps"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONTAINER_APP_NAME", previous);
        }
    }
}
