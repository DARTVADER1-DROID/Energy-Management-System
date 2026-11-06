using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Households;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Devices;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var households = endpoints.MapGroup("/api/households").WithTags("Households");
        households.MapPost("/", CreateHouseholdAsync);
        households.MapGet("/", ListHouseholdsAsync);
        households.MapGet("/{householdId:int}", GetHouseholdAsync);

        var devices = endpoints.MapGroup("/api/devices").WithTags("Devices");
        devices.MapPost("/{householdId:int}", RegisterDeviceAsync);
        devices.MapGet("/", ListDevicesAsync);
        devices.MapGet("/{deviceCode}", GetDeviceAsync);
        devices.MapPatch("/{deviceCode}/deactivate", DeactivateDeviceAsync);

        return endpoints;
    }

    private static async Task<Results<Created<HouseholdResponse>, ValidationProblem>> CreateHouseholdAsync(
        CreateHouseholdRequest request,
        DeviceRegistrationService service,
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
        return TypedResults.Created(
            $"/api/households/{household.Id}",
            new HouseholdResponse(household.Id, household.HouseholdCode, household.Name));
    }

    private static async Task<Ok<IReadOnlyList<HouseholdResponse>>> ListHouseholdsAsync(
        EnergyManagementDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var households = await dbContext.Households
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<HouseholdResponse>>(
            households.Select(item => new HouseholdResponse(
                item.Id, item.HouseholdCode, item.Name)).ToArray());
    }


    private static async Task<Results<Ok<HouseholdResponse>, NotFound>> GetHouseholdAsync(
        int householdId,
        EnergyManagementDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var household = await dbContext.Households
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == householdId, cancellationToken);

        return household is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new HouseholdResponse(
                household.Id, household.HouseholdCode, household.Name));
    }

    private static async Task<Results<Created<DeviceResponse>, NotFound<string>, ValidationProblem>> RegisterDeviceAsync(
        int householdId,
        RegisterDeviceRequest request,
        DeviceRegistrationService service,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            var device = await service.RegisterAsync(householdId, request, cancellationToken);
            return TypedResults.Created($"/api/devices/{device.DeviceCode}", DeviceResponse.From(device));
        }
        catch (HouseholdNotFoundException exception)
        {
            return TypedResults.NotFound(exception.Message);
        }
    }

    private static async Task<Ok<IReadOnlyList<DeviceResponse>>> ListDevicesAsync(
        int? householdId,
        EnergyManagementDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Devices.AsNoTracking();
        if (householdId.HasValue)
        {
            query = query.Where(device => device.HouseholdId == householdId.Value);
        }

        var devices = await query.OrderBy(device => device.DeviceCode).ToListAsync(cancellationToken);
        return TypedResults.Ok<IReadOnlyList<DeviceResponse>>(
            devices.Select(DeviceResponse.From).ToArray());
    }

    private static async Task<Results<Ok<DeviceResponse>, NotFound>> GetDeviceAsync(
        string deviceCode,
        EnergyManagementDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.DeviceCode == deviceCode, cancellationToken);

        return device is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(DeviceResponse.From(device));
    }

    private static async Task<Results<NoContent, NotFound>> DeactivateDeviceAsync(
        string deviceCode,
        EnergyManagementDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices
            .SingleOrDefaultAsync(item => item.DeviceCode == deviceCode, cancellationToken);

        if (device is null)
        {
            return TypedResults.NotFound();
        }

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
        {
            errors["electricalConfiguration"] = ["A supported electrical configuration is required."];
        }

        if (request.Description?.Trim().Length > 1000)
        {
            errors["description"] = ["Description must not exceed 1000 characters."];
        }

        var thresholds = request.Thresholds;
        if (thresholds is not null)
        {
            ValidateNonNegative(errors, nameof(thresholds.MinimumVoltage), thresholds.MinimumVoltage);
            ValidateNonNegative(errors, nameof(thresholds.MaximumVoltage), thresholds.MaximumVoltage);
            ValidateNonNegative(errors, nameof(thresholds.MaximumCurrent), thresholds.MaximumCurrent);
            ValidateNonNegative(errors, nameof(thresholds.MaximumInstantaneousPower), thresholds.MaximumInstantaneousPower);
            ValidateNonNegative(errors, nameof(thresholds.MaximumDailyEnergy), thresholds.MaximumDailyEnergy);
            ValidateNonNegative(errors, nameof(thresholds.MaximumTemperature), thresholds.MaximumTemperature);

            if (thresholds.MinimumVoltage is not null &&
                thresholds.MaximumVoltage is not null &&
                thresholds.MinimumVoltage >= thresholds.MaximumVoltage)
            {
                errors["thresholds.minimumVoltage"] = ["Minimum voltage must be lower than maximum voltage."];
            }

            var hasThreshold = thresholds.MinimumVoltage is not null ||
                               thresholds.MaximumVoltage is not null ||
                               thresholds.MaximumCurrent is not null ||
                               thresholds.MaximumInstantaneousPower is not null ||
                               thresholds.MaximumDailyEnergy is not null ||
                               thresholds.MaximumTemperature is not null;

            if (hasThreshold && thresholds.WarningPersistenceSeconds is null)
            {
                errors["thresholds.warningPersistenceSeconds"] =
                    ["A warning persistence duration is required when any threshold is configured."];
            }

            if (thresholds.WarningPersistenceSeconds is <= 0 or > 86400)
            {
                errors["thresholds.warningPersistenceSeconds"] =
                    ["Warning persistence duration must be between 1 and 86400 seconds."];
            }
        }

        return errors;
    }

    private static void AddRequired(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            errors[field] = [$"A value is required and must not exceed {maximumLength} characters."];
        }
    }

    private static void ValidateNonNegative(
        IDictionary<string, string[]> errors,
        string field,
        decimal? value)
    {
        if (value < 0)
        {
            errors[$"thresholds.{field}"] = ["Threshold values cannot be negative."];
        }
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
    DeviceThresholds Thresholds,
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
        device.Thresholds,
        device.IsActive,
        device.RegisteredAtUtc,
        device.LastSeenAtUtc);
}
