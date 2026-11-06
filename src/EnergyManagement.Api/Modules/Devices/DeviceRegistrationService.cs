using System.Data;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Households;
using Microsoft.EntityFrameworkCore;

namespace EnergyManagement.Api.Modules.Devices;

public sealed class DeviceRegistrationService(EnergyManagementDbContext dbContext)
{
    public async Task<Household> CreateHouseholdAsync(string name, CancellationToken cancellationToken)
    {
        var household = new Household(name);
        dbContext.Households.Add(household);
        await dbContext.SaveChangesAsync(cancellationToken);
        return household;
    }

    public async Task<Device> RegisterAsync(
        int householdId,
        RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        // Serializable isolation and a concurrency token protect sequential
        // number allocation when multiple registrations target one household.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var household = await dbContext.Households
            .SingleOrDefaultAsync(item => item.Id == householdId, cancellationToken)
            ?? throw new HouseholdNotFoundException(householdId);

        var deviceNumber = household.AllocateDeviceNumber();
        var device = new Device(
            household,
            request.Name,
            request.HardwareModel,
            request.FirmwareVersion,
            request.InstallationLocation,
            request.ElectricalConfiguration,
            request.Description,
            request.Thresholds,
            deviceNumber);

        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return device;
    }
}

public sealed class HouseholdNotFoundException(int householdId)
    : Exception($"Household {householdId} was not found.");
