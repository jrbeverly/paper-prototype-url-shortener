using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Endpoints;

/// <summary>Operational health check endpoints for liveness and dependency monitoring.</summary>
public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", async (IHealthCheckService healthCheck, CancellationToken ct) =>
        {
            var report = await healthCheck.GetHealthAsync(ct);
            return Results.Ok(report);
        })
            .WithTags("Health")
            .WithName("GetHealth")
            .WithOpenApi()
            .WithDescription("Basic liveness check. Returns 200 OK when the service is running.")
            .Produces<HealthReport>(StatusCodes.Status200OK);

        app.MapGet("/health/details", async (IHealthCheckService healthCheck, CancellationToken ct) =>
        {
            var report = await healthCheck.GetDetailedHealthAsync(ct);
            var statusCode = report.Status switch
            {
                "unhealthy" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status200OK
            };
            return Results.Json(report, statusCode: statusCode);
        })
            .WithTags("Health")
            .WithName("GetHealthDetails")
            .WithOpenApi()
            .WithDescription("Detailed dependency health check. Reports connectivity status for DynamoDB and Postgres. Returns 503 if any dependency is unhealthy.")
            .Produces<DetailedHealthReport>(StatusCodes.Status200OK)
            .Produces<DetailedHealthReport>(StatusCodes.Status503ServiceUnavailable);
    }
}
