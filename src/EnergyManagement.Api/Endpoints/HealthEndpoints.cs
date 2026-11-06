using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () =>
            TypedResults.Ok(new
            {
                Status = "Healthy",
                TimestampUtc = DateTimeOffset.UtcNow
            }))
            .WithName("GetLiveness")
            .WithTags("Health")
            .AllowAnonymous();

        endpoints.MapGet("/health/ready", GetReadinessAsync)
            .WithName("GetReadiness")
            .WithTags("Health")
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        EnergyManagementDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? TypedResults.Ok(new { Status = "Ready" })
            : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
