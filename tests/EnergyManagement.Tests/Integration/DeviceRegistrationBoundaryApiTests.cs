using System.Net;
using System.Net.Http.Json;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnergyManagement.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class DeviceRegistrationBoundaryApiTests(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private EnergyManagementApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new EnergyManagementApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identityDb.Database.MigrateAsync();
        identityDb.Users.Add(new ApplicationUser
        {
            Id = 1,
            UserName = "boundary-admin@example.test",
            Email = "boundary-admin@example.test",
            EmailConfirmed = true,
            TwoFactorEnabled = true
        });
        identityDb.Roles.Add(new IdentityRole<int>("Administrator")
        {
            Id = 1,
            NormalizedName = "ADMINISTRATOR"
        });
        identityDb.UserRoles.Add(new IdentityUserRole<int> { UserId = 1, RoleId = 1 });
        await identityDb.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateHousehold_TrimsNameAndReturnsLocationHeader()
    {
        var response = await _client.PostAsJsonAsync("/api/households/", new { name = "  Family Home  " });
        var household = await response.Content.ReadFromJsonAsync<HouseholdResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(household);
        Assert.Equal("Family Home", household.Name);
        Assert.Equal($"/api/households/{household.Id}", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateHousehold_RejectsBlankName(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/households/", new { name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateHousehold_RejectsNameLongerThan120Characters()
    {
        var response = await _client.PostAsJsonAsync("/api/households/", new { name = new string('H', 121) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateHousehold_AcceptsNameAtMaximumLength()
    {
        var name = new string('H', 120);
        var response = await _client.PostAsJsonAsync("/api/households/", new { name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(name, (await response.Content.ReadFromJsonAsync<HouseholdResponse>())?.Name);
    }

    [Fact]
    public async Task ListHouseholds_ReturnsCreatedHouseholdsInIdOrder()
    {
        await CreateHouseholdAsync("First");
        await CreateHouseholdAsync("Second");

        var response = await _client.GetAsync("/api/households/");
        var households = await response.Content.ReadFromJsonAsync<HouseholdResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(households);
        Assert.Equal(new[] { "First", "Second" }, households.Select(item => item.Name));
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("hardwareModel", "")]
    [InlineData("firmwareVersion", "")]
    [InlineData("installationLocation", "")]
    public async Task RegisterDevice_RejectsBlankRequiredMetadata(string field, string value)
    {
        var householdId = await CreateHouseholdAsync("Validation Home");
        var request = ValidRequest() with
        {
            Name = field == "name" ? value : "Monitor",
            HardwareModel = field == "hardwareModel" ? value : "ESP32",
            FirmwareVersion = field == "firmwareVersion" ? value : "1.0",
            InstallationLocation = field == "installationLocation" ? value : "Main DB"
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(121, "name")]
    [InlineData(81, "hardwareModel")]
    [InlineData(81, "firmwareVersion")]
    [InlineData(161, "installationLocation")]
    public async Task RegisterDevice_RejectsMetadataOverMaximumLength(int length, string field)
    {
        var householdId = await CreateHouseholdAsync("Length Home");
        var value = new string('X', length);
        var request = ValidRequest() with
        {
            Name = field == "name" ? value : "Monitor",
            HardwareModel = field == "hardwareModel" ? value : "ESP32",
            FirmwareVersion = field == "firmwareVersion" ? value : "1.0",
            InstallationLocation = field == "installationLocation" ? value : "Main DB"
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_AcceptsMetadataAtMaximumLengths()
    {
        var householdId = await CreateHouseholdAsync("Boundary Home");
        var request = new RegisterDeviceRequest(
            new string('N', 120), new string('M', 80), new string('F', 80),
            new string('L', 160), ElectricalConfiguration.SinglePhase,
            new string('D', 1000), new DeviceThresholdsRequest());

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var device = await response.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.NotNull(device);
        Assert.Equal(120, device.Name.Length);
        Assert.Equal(80, device.HardwareModel.Length);
        Assert.Equal(80, device.FirmwareVersion.Length);
        Assert.Equal(160, device.InstallationLocation.Length);
        Assert.Equal(1000, device.Description?.Length);
    }

    [Fact]
    public async Task RegisterDevice_RejectsDescriptionOverMaximumLength()
    {
        var householdId = await CreateHouseholdAsync("Description Home");
        var request = ValidRequest() with { Description = new string('D', 1001) };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_RejectsUnsupportedElectricalConfiguration()
    {
        var householdId = await CreateHouseholdAsync("Electrical Home");
        var request = ValidRequest() with { ElectricalConfiguration = (ElectricalConfiguration)99 };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_RejectsNegativeThreshold()
    {
        var householdId = await CreateHouseholdAsync("Threshold Home");
        var request = ValidRequest() with
        {
            Thresholds = new DeviceThresholdsRequest { MaximumCurrent = -0.01m, WarningPersistenceSeconds = 1 }
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(230, 230)]
    [InlineData(250, 230)]
    public async Task RegisterDevice_RejectsInvalidVoltageRange(decimal minimum, decimal maximum)
    {
        var householdId = await CreateHouseholdAsync("Voltage Home");
        var request = ValidRequest() with
        {
            Thresholds = new DeviceThresholdsRequest
            {
                MinimumVoltage = minimum,
                MaximumVoltage = maximum,
                WarningPersistenceSeconds = 10
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86401)]
    public async Task RegisterDevice_RejectsOutOfRangePersistenceDuration(int duration)
    {
        var householdId = await CreateHouseholdAsync("Duration Home");
        var request = ValidRequest() with
        {
            Thresholds = new DeviceThresholdsRequest
            {
                MaximumCurrent = 10,
                WarningPersistenceSeconds = duration
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_AcceptsPersistenceDurationAtBothBounds()
    {
        var householdId = await CreateHouseholdAsync("Duration Bounds Home");
        var low = ValidRequest() with
        {
            Thresholds = new DeviceThresholdsRequest { MaximumCurrent = 10, WarningPersistenceSeconds = 1 }
        };
        var high = ValidRequest() with
        {
            Thresholds = new DeviceThresholdsRequest { MaximumCurrent = 10, WarningPersistenceSeconds = 86400 }
        };

        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync($"/api/devices/{householdId}", low)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync($"/api/devices/{householdId}", high)).StatusCode);
    }

    [Fact]
    public async Task RegisterDevice_AcceptsThresholdsWithoutOptionalTemperature()
    {
        var householdId = await CreateHouseholdAsync("Optional Temperature Home");
        var request = ValidRequest() with
        {
            Thresholds = new DeviceThresholdsRequest
            {
                MinimumVoltage = 180,
                MaximumVoltage = 260,
                MaximumCurrent = 40,
                MaximumInstantaneousPower = 9000,
                MaximumDailyEnergy = 50,
                WarningPersistenceSeconds = 60
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", request);
        var device = await response.Content.ReadFromJsonAsync<DeviceResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(device);
        Assert.Null(device.Thresholds.MaximumTemperature);
    }

    [Fact]
    public async Task RegisterDevice_InvalidRequestDoesNotConsumeDeviceNumber()
    {
        var householdId = await CreateHouseholdAsync("No Gap Home");
        var invalid = ValidRequest() with { Name = " " };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync($"/api/devices/{householdId}", invalid)).StatusCode);

        var validResponse = await _client.PostAsJsonAsync($"/api/devices/{householdId}", ValidRequest());
        var device = await validResponse.Content.ReadFromJsonAsync<DeviceResponse>();

        Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);
        Assert.NotNull(device);
        Assert.Equal("H001-DEV001", device.DeviceCode);
    }

    [Fact]
    public async Task RegisterDevice_NumbersAreIndependentPerHousehold()
    {
        var firstHousehold = await CreateHouseholdAsync("First Numbering Home");
        var secondHousehold = await CreateHouseholdAsync("Second Numbering Home");

        var first = await RegisterDeviceAsync(firstHousehold);
        var second = await RegisterDeviceAsync(secondHousehold);

        Assert.Equal("H001-DEV001", first.DeviceCode);
        Assert.Equal("H002-DEV001", second.DeviceCode);
    }

    [Fact]
    public async Task GetDevice_UnknownCodeReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/devices/H999-DEV999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateDevice_UnknownCodeReturnsNotFound()
    {
        var response = await _client.PatchAsync("/api/devices/H999-DEV999/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateDevice_IsIdempotentAndDeviceRemainsInList()
    {
        var householdId = await CreateHouseholdAsync("Inactive Listing Home");
        var device = await RegisterDeviceAsync(householdId);

        var first = await _client.PatchAsync($"/api/devices/{device.DeviceCode}/deactivate", null);
        var second = await _client.PatchAsync($"/api/devices/{device.DeviceCode}/deactivate", null);
        var list = await _client.GetFromJsonAsync<DeviceResponse[]>("/api/devices");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.NotNull(list);
        Assert.False(Assert.Single(list).IsActive);
    }

    [Fact]
    public async Task ListDevices_WithoutFilterReturnsDevicesAcrossHouseholds()
    {
        var firstHousehold = await CreateHouseholdAsync("List Home A");
        var secondHousehold = await CreateHouseholdAsync("List Home B");
        await RegisterDeviceAsync(firstHousehold);
        await RegisterDeviceAsync(secondHousehold);

        var response = await _client.GetAsync("/api/devices/");
        var devices = await response.Content.ReadFromJsonAsync<DeviceResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(devices);
        Assert.Equal(2, devices.Length);
        Assert.Equal(new[] { "H001-DEV001", "H002-DEV001" }, devices.Select(item => item.DeviceCode));
    }

    [Fact]
    public async Task ListDevices_UnknownHouseholdFilterReturnsEmptyList()
    {
        var devices = await _client.GetFromJsonAsync<DeviceResponse[]>("/api/devices?householdId=9999");
        Assert.NotNull(devices);
        Assert.Empty(devices);
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
        return (await response.Content.ReadFromJsonAsync<HouseholdResponse>())!.Id;
    }

    private async Task<DeviceResponse> RegisterDeviceAsync(int householdId)
    {
        var response = await _client.PostAsJsonAsync($"/api/devices/{householdId}", ValidRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceResponse>())!;
    }

    private static RegisterDeviceRequest ValidRequest() => new(
        "Main Energy Monitor", "ESP32", "1.0.0", "Main Distribution Board",
        ElectricalConfiguration.SinglePhase, null, new DeviceThresholdsRequest());
}
