using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Resources;

namespace ProgrammePulse.Startup;

/// <summary>
/// Wires ASP.NET Core application telemetry into Azure Monitor / Application
/// Insights when a connection string is supplied. The default is to stay
/// disabled so local development continues without a cloud monitoring endpoint.
/// </summary>
public static class OpenTelemetryBootstrap
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var settings = OpenTelemetrySettings.FromConfiguration(builder.Configuration);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: settings.ServiceName,
                    serviceVersion: settings.ServiceVersion,
                    serviceInstanceId: Environment.MachineName);
            })
            .UseAzureMonitor(options =>
            {
                options.ConnectionString = settings.ConnectionString;
            });
    }
}

public sealed class OpenTelemetrySettings
{
    public const string SectionName = "OpenTelemetry";

    public static OpenTelemetrySettings FromConfiguration(IConfiguration configuration)
    {
        var connectionString = FirstNonEmpty(
            configuration.GetSection(SectionName).GetSection("ApplicationInsights")["ConnectionString"],
            configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"],
            configuration["OpenTelemetry:ApplicationInsights:ConnectionString"],
            configuration["OpenTelemetry__ApplicationInsights__ConnectionString"],
            configuration["AzureMonitor:ConnectionString"],
            configuration["AzureMonitor__ConnectionString"]);

        return new OpenTelemetrySettings
        {
            ConnectionString = connectionString,
            ServiceName = FirstNonEmpty(
                configuration["OpenTelemetry:ServiceName"],
                configuration["OpenTelemetry__ServiceName"],
                configuration["ApplicationName"],
                configuration["ASPNETCORE_APP_NAME"]) ?? "ProgrammePulse",
            ServiceVersion = FirstNonEmpty(
                configuration["OpenTelemetry:ServiceVersion"],
                configuration["OpenTelemetry__ServiceVersion"],
                configuration["Version"]) ?? "1.0.0",
            Enabled = !string.IsNullOrWhiteSpace(connectionString)
        };
    }

    public string? ConnectionString { get; init; }

    public string ServiceName { get; init; } = "ProgrammePulse";

    public string ServiceVersion { get; init; } = "1.0.0";

    public bool Enabled { get; init; }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
