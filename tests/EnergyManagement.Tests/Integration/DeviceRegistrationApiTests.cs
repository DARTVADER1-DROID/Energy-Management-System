using System.Net;
using System.Net.Http.Json;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class DeviceRegistrationApiTests(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private readonly EnergyManagementApiFactory _factory = new(postgres.ConnectionString);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await ResetDatabaseAsync();
    }

    [Fact]
    public async Task CreateHousehold_ReturnsGeneratedHouseholdCode()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/households/",
            new { name = "Family Home" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var household = await response.Content.ReadFromJsonAsync<HouseholdResponse>();

        Assert.NotNull(household);
        Assert.Equal("H001", household.HouseholdCode);
        Assert.Equal("Family Home", household.Name);
    }

    [Fact]
    public async Task CreateHousehold_RejectsBlankName()
    {
        var response = await _client.PostAsJsonAsync("/api/households/", new { name = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_GeneratesCodeAndPersistsMetadata()
    {
        var householdId = await CreateHouseholdAsync("Family Home");
        var request = ValidRequest();

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var device = await response.Content.ReadFromJsonAsync<DeviceResponse>();

        Assert.NotNull(device);
        Assert.Equal("H001-DEV001", device.DeviceCode);
        Assert.Equal("Main Energy Monitor", device.Name);
        Assert.Equal(ElectricalConfiguration.SinglePhase, device.ElectricalConfiguration);
        Assert.True(device.IsActive);
    }

    [Fact]
    public async Task RegisterDevice_UnknownHouseholdReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/devices/9999", ValidRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_InvalidMetadataReturnsBadRequest()
    {
        var householdId = await CreateHouseholdAsync("Family Home");
        var request = new
        {
            name = " ",
            hardwareModel = "ESP32",
            firmwareVersion = "1.0",
            installationLocation = "Main DB",
            electricalConfiguration = ElectricalConfiguration.SinglePhase
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_ThresholdWithoutPersistenceDurationReturnsBadRequest()
    {
        var householdId = await CreateHouseholdAsync("Family Home");
        var request = new
        {
            name = "Monitor",
            hardwareModel = "ESP32",
            firmwareVersion = "1.0",
            installationLocation = "Main DB",
            electricalConfiguration = ElectricalConfiguration.SinglePhase,
            thresholds = new { maximumCurrent = 20 }
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateDevice_KeepsDeviceRetrievableAndInactive()
    {
        var householdId = await CreateHouseholdAsync("Family Home");
        var registerResponse = await _client.PostAsJsonAsync(
            $"/api/devices/{householdId}", ValidRequest());
        var registered = await registerResponse.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.NotNull(registered);

        var deactivateResponse = await _client.PatchAsync(
            $"/api/devices/{registered.DeviceCode}/deactivate", content: null);
        var getResponse = await _client.GetAsync($"/api/devices/{registered.DeviceCode}");
        var retrieved = await getResponse.Content.ReadFromJsonAsync<DeviceResponse>();

        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(retrieved);
        Assert.False(retrieved.IsActive);
        Assert.Equal(registered.DeviceCode, retrieved.DeviceCode);
    }

    [Fact]
    public async Task ListDevices_CanFilterByHousehold()
    {
        var firstHouseholdId = await CreateHouseholdAsync("First Home");
        var secondHouseholdId = await CreateHouseholdAsync("Second Home");

        await _client.PostAsJsonAsync($"/api/devices/{firstHouseholdId}", ValidRequest());
        await _client.PostAsJsonAsync($"/api/devices/{secondHouseholdId}", ValidRequest());

        var response = await _client.GetAsync($"/api/devices?householdId={firstHouseholdId}");
        var devices = await response.Content.ReadFromJsonAsync<DeviceResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(devices);
        var device = Assert.Single(devices);
        Assert.Equal(firstHouseholdId, device.HouseholdId);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<int> CreateHouseholdAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/households/", new { name });
        response.EnsureSuccessStatusCode();
        var household = await response.Content.ReadFromJsonAsync<HouseholdResponse>();
        Assert.NotNull(household);
        return household.Id;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }

    private static RegisterDeviceRequest ValidRequest() => new(
        "Main Energy Monitor",
        "ESP32",
        "1.0.0",
        "Main Distribution Board",
        ElectricalConfiguration.SinglePhase,
        null,
        new DeviceThresholds());
}
