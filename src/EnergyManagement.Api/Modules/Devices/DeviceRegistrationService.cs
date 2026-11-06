using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Households;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnergyManagement.Api.Modules.Devices;

public sealed class DeviceRegistrationService(EnergyManagementDbContext dbContext)
{
    private const int MaximumAllocationAttempts = 4;

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
        for (var attempt = 1; attempt <= MaximumAllocationAttempts; attempt++)
        {
            try
            {
                return await RegisterOnceAsync(householdId, request, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaximumAllocationAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception)
                when (IsSerializationFailure(exception) && attempt < MaximumAllocationAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (PostgresException exception)
                when (exception.SqlState == PostgresErrorCodes.SerializationFailure &&
                      attempt < MaximumAllocationAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Device registration retry limit was exceeded.");
    }

    private async Task<Device> RegisterOnceAsync(
        int householdId,
        RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        // The UPDATE obtains a PostgreSQL row lock and increments the counter
        // atomically. Concurrent registrations therefore cannot read the same
        // device number. The counter update and device insert share a transaction.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var deviceNumber = await dbContext.Database
            .SqlQuery<int>($"""
                UPDATE "households"
                SET "NextDeviceNumber" = "NextDeviceNumber" + 1
                WHERE "Id" = {householdId}
                RETURNING "NextDeviceNumber" - 1 AS "Value"
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (deviceNumber == 0)
        {
            throw new HouseholdNotFoundException(householdId);
        }

        var household = await dbContext.Households
            .SingleAsync(item => item.Id == householdId, cancellationToken);

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

    private static bool IsSerializationFailure(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure
        };
}

public sealed class HouseholdNotFoundException(int householdId)
    : Exception($"Household {householdId} was not found.");
