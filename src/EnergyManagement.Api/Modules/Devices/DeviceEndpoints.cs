using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Households;
using EnergyManagement.Api.Modules.Identity;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Devices;

public static class DeviceEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var households = endpoints.MapGroup("/api/households")
            .WithTags("Households")
            .RequireAuthorization();

        households.MapPost("/", CreateHouseholdAsync)
            .RequireAuthorization("HouseholdAdministrator");
        households.MapGet("/", ListHouseholdsAsync);
        households.MapGet("/{householdId:int}", GetHouseholdAsync);

        var devices = endpoints.MapGroup("/api/devices")
            .WithTags("Devices")
            .RequireAuthorization();

        devices.MapPost("/{householdId:int}", RegisterDeviceAsync)
            .RequireAuthorization("HouseholdAdministrator");
        devices.MapGet("/", ListDevicesAsync);
        devices.MapGet("/{deviceCode}", GetDeviceAsync);
        devices.MapPatch("/{deviceCode}/deactivate", DeactivateDeviceAsync)
            .RequireAuthorization("HouseholdAdministrator");

        return endpoints;
    }

    private static async Task<Results<Created<HouseholdResponse>, ValidationProblem>> CreateHouseholdAsync(
        CreateHouseholdRequest request,
        DeviceRegistrationService service,
        HouseholdAccessService householdAccess,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required and must not exceed 120 characters."]
            });
        }

        var household = await service.CreateHouseholdAsync(request.Name, cancellationToken);
        await householdAccess.AddMembershipAsync(
            GetUserId(user), household.Id, HouseholdRole.Administrator, cancellationToken);
        return TypedResults.Created(
            $"/api/households/{household.Id}",
            new HouseholdResponse(household.Id, household.HouseholdCode, household.Name));
    }

    private static async Task<Ok<IReadOnlyList<HouseholdResponse>>> ListHouseholdsAsync(
        EnergyManagementDbContext dbContext,
        HouseholdAccessService householdAccess,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var householdIds = await householdAccess.GetHouseholdIdsAsync(GetUserId(user), cancellationToken);
        var households = await dbContext.Households
            .AsNoTracking()
            .Where(item => householdIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<HouseholdResponse>>(
            households.Select(item => new HouseholdResponse(
                item.Id, item.HouseholdCode, item.Name)).ToArray());
    }

    private static async Task<Results<Ok<HouseholdResponse>, NotFound>> GetHouseholdAsync(
        int householdId,
        EnergyManagementDbContext dbContext,
        HouseholdAccessService householdAccess,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await householdAccess.IsMemberAsync(GetUserId(user), householdId, cancellationToken))
            return TypedResults.NotFound();

        var household = await dbContext.Households
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == householdId, cancellationToken);

        return household is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new HouseholdResponse(
                household.Id, household.HouseholdCode, household.Name));
    }

    private static async Task<Results<Created<DeviceResponse>, NotFound<string>, ValidationProblem, ForbidHttpResult>> RegisterDeviceAsync(
        int householdId,
        RegisterDeviceRequest request,
        DeviceRegistrationService service,
        EnergyManagementDbContext dbContext,
        HouseholdAccessService householdAccess,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Households.AsNoTracking()
                .AnyAsync(household => household.Id == householdId, cancellationToken))
            return TypedResults.NotFound("Household was not found.");

        if (!await householdAccess.IsAdministratorAsync(GetUserId(user), householdId, cancellationToken))
            return TypedResults.Forbid();

        var errors = Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        try
        {
            var device = await service.RegisterAsync(householdId, request, cancellationToken);
            return TypedResults.Created($"/api/devices/{device.DeviceCode}", DeviceResponse.From(device));
        }
        catch (HouseholdNotFoundException exception)
        {
            return TypedResults.NotFound(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["thresholds"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> ListDevicesAsync(
        int? householdId,
        EnergyManagementDbContext dbContext,
        HouseholdAccessService householdAccess,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || page > int.MaxValue / Math.Max(pageSize, 1))
            errors["page"] = ["Page must be a positive value within the supported range."];
        if (pageSize is < 1 or > MaximumPageSize)
            errors["pageSize"] = [$"Page size must be between 1 and {MaximumPageSize}."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var accessibleHouseholds = await householdAccess.GetHouseholdIdsAsync(
            GetUserId(user), cancellationToken);
        var query = dbContext.Devices.AsNoTracking()
            .Where(device => accessibleHouseholds.Contains(device.HouseholdId));
        if (householdId.HasValue)
            query = query.Where(device => device.HouseholdId == householdId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var devices = await query.OrderBy(device => device.DeviceCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString();
        httpContext.Response.Headers["X-Page"] = page.ToString();
        httpContext.Response.Headers["X-Page-Size"] = pageSize.ToString();
        httpContext.Response.Headers["X-Total-Pages"] =
            Math.Ceiling(totalCount / (double)pageSize).ToString("0");

        return TypedResults.Ok<IReadOnlyList<DeviceResponse>>(
            devices.Select(DeviceResponse.From).ToArray());
    }

    private static async Task<IResult> GetDeviceAsync(
        string deviceCode,
        EnergyManagementDbContext dbContext,
        HouseholdAccessService householdAccess,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DeviceCode == deviceCode, cancellationToken);

        if (device is null || !await householdAccess.IsMemberAsync(
                GetUserId(user), device.HouseholdId, cancellationToken))
            return TypedResults.NotFound();

        return TypedResults.Ok(DeviceResponse.From(device));
    }

    private static async Task<IResult> DeactivateDeviceAsync(
        string deviceCode,
        EnergyManagementDbContext dbContext,
        HouseholdAccessService householdAccess,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .SingleOrDefaultAsync(item => item.DeviceCode == deviceCode, cancellationToken);

        if (device is null) return TypedResults.NotFound();
        if (!await householdAccess.IsAdministratorAsync(
                GetUserId(user), device.HouseholdId, cancellationToken))
            return TypedResults.Forbid();

        device.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static Dictionary<string, string[]> Validate(RegisterDeviceRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequired(errors, "name", request.Name, 120);
        AddRequired(errors, "hardwareModel", request.HardwareModel, 80);
        AddRequired(errors, "firmwareVersion", request.FirmwareVersion, 80);
        AddRequired(errors, "installationLocation", request.InstallationLocation, 160);

        if (!Enum.IsDefined(request.ElectricalConfiguration))
            errors["electricalConfiguration"] = ["A supported electrical configuration is required."];
        if (request.Description?.Trim().Length > 1000)
            errors["description"] = ["Description must not exceed 1000 characters."];

        return errors;
    }

    private static int GetUserId(ClaimsPrincipal user)
    {
        var identifier = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(identifier, out var userId)
            ? userId
            : throw new InvalidOperationException("The authenticated principal has no valid user identifier.");
    }

    private static void AddRequired(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
            errors[field] = [$"A value is required and must not exceed {maximumLength} characters."];
    }
}

public sealed record HouseholdResponse(int Id, string HouseholdCode, string Name);

public sealed record DeviceResponse(
    int Id,
    int HouseholdId,
    string DeviceCode,
    string Name,
    string HardwareModel,
    string FirmwareVersion,
    string InstallationLocation,
    ElectricalConfiguration ElectricalConfiguration,
    string? Description,
    DeviceThresholdsResponse Thresholds,
    bool IsActive,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc)
{
    public static DeviceResponse From(Device device) => new(
        device.Id,
        device.HouseholdId,
        device.DeviceCode,
        device.Name,
        device.HardwareModel,
        device.FirmwareVersion,
        device.InstallationLocation,
        device.ElectricalConfiguration,
        device.Description,
        new DeviceThresholdsResponse(
            device.Thresholds.MinimumVoltage,
            device.Thresholds.MaximumVoltage,
            device.Thresholds.MaximumCurrent,
            device.Thresholds.MaximumInstantaneousPower,
            device.Thresholds.MaximumDailyEnergy,
            device.Thresholds.MaximumTemperature,
            device.Thresholds.WarningPersistenceSeconds),
        device.IsActive,
        device.RegisteredAtUtc,
        device.LastSeenAtUtc);
}
