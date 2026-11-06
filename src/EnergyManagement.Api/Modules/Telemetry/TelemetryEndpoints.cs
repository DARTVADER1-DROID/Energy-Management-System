using System.Security.Claims;
using System.Text.Json;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Telemetry;

public static class TelemetryEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/device-telemetry", IngestAsync)
            .WithTags("Telemetry")
            .AllowAnonymous()
            .RequireRateLimiting("device-hmac");

        endpoints.MapGet("/api/devices/{deviceCode}/telemetry", GetDeviceTelemetryAsync)
            .WithTags("Telemetry")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> IngestAsync(
        HttpContext httpContext,
        DeviceAuthenticationService authenticationService,
        TelemetryService telemetryService,
        CancellationToken cancellationToken)
    {
        var device = await authenticationService.AuthenticateRequestAsync(httpContext, cancellationToken);
        if (device is null)
            return TypedResults.Unauthorized();

        if (!httpContext.Request.HasJsonContentType())
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        TelemetryEnvelope? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<TelemetryEnvelope>(
                httpContext.Request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest(new { error = "The telemetry payload is not valid JSON." });
        }

        if (envelope is null)
            return TypedResults.BadRequest(new { error = "A telemetry envelope is required." });

        try
        {
            var result = await telemetryService.IngestAsync(device, envelope, cancellationToken);
            httpContext.Response.Headers.CacheControl = "no-store";
            return TypedResults.Accepted($"/api/devices/{device.DeviceCode}/telemetry", result);
        }
        catch (TelemetryValidationException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.Field] = [exception.Message]
            });
        }
        catch (DuplicateTelemetrySampleException)
        {
            return TypedResults.Conflict(new { error = "This telemetry sample has already been received." });
        }
    }

    private static async Task<IResult> GetDeviceTelemetryAsync(
        string deviceCode,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int? limit,
        EnergyManagementDbContext dbContext,
        HouseholdAccessService householdAccess,
        TelemetryService telemetryService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DeviceCode == deviceCode, cancellationToken);
        if (device is null ||
            !await householdAccess.IsMemberAsync(GetUserId(user), device.HouseholdId, cancellationToken))
            return TypedResults.NotFound();

        try
        {
            var readings = await telemetryService.GetForDeviceAsync(
                device.Id, fromUtc, toUtc, limit ?? 200, cancellationToken);
            return TypedResults.Ok(readings);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.BadRequest(new { error = exception.Message });
        }
    }

    private static int GetUserId(ClaimsPrincipal user)
    {
        var identifier = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(identifier, out var userId)
            ? userId
            : throw new InvalidOperationException("The authenticated principal has no valid user identifier.");
    }
}
