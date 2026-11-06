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
            .WithTags("Health");

        return endpoints;
    }
}
