using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Households;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class DeviceRegistrationConcurrencyTests(PostgreSqlFixture postgres)
    : IAsyncLifetime
{
    private readonly EnergyManagementApiFactory _factory = new(postgres.ConnectionString);
    private int _householdId;

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();

        var household = new Household("Concurrent Registration Home");
        dbContext.Households.Add(household);
        await dbContext.SaveChangesAsync();
        _householdId = household.Id;
    }

    [Fact]
    public async Task RegisteringManyDevicesConcurrently_AllocatesUniqueSequentialCodes()
    {
        const int deviceCount = 20;

        var registrations = Enumerable.Range(0, deviceCount)
            .Select(index => RegisterInIndependentScopeAsync(index));

        var devices = await Task.WhenAll(registrations);

        Assert.Equal(deviceCount, devices.Select(device => device.DeviceCode).Distinct().Count());
        Assert.Equal(
            Enumerable.Range(1, deviceCount).Select(number => $"H001-DEV{number:000}"),
            devices.Select(device => device.DeviceCode).OrderBy(code => code));

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        var persistedDevices = await verificationDb.Devices
            .Where(device => device.HouseholdId == _householdId)
            .ToListAsync();

        Assert.Equal(deviceCount, persistedDevices.Count);
    }

    [Fact]
    public async Task DeactivatedDeviceNumber_IsNotReused()
    {
        var first = await RegisterInIndependentScopeAsync(1);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
            var device = await dbContext.Devices.SingleAsync(item => item.DeviceCode == first.DeviceCode);
            device.Deactivate();
            await dbContext.SaveChangesAsync();
        }

        var second = await RegisterInIndependentScopeAsync(2);

        Assert.Equal("H001-DEV001", first.DeviceCode);
        Assert.Equal("H001-DEV002", second.DeviceCode);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<Device> RegisterInIndependentScopeAsync(int index)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeviceRegistrationService>();
        return await service.RegisterAsync(
            _householdId,
            new RegisterDeviceRequest(
                $"Monitor {index}",
                "ESP32",
                "1.0.0",
                "Main Distribution Board",
                ElectricalConfiguration.SinglePhase,
                null,
                null),
            CancellationToken.None);
    }
}
