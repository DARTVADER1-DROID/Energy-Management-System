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
public sealed class DeviceRegistrationApiTests(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private EnergyManagementApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new EnergyManagementApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();
        await ResetDatabaseAsync();
    }


    [Fact]
    public async Task AdministratorWithoutMfa_CannotAccessProtectedApi()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var administrator = await identityDb.Users.SingleAsync(user => user.Id == 1);
            administrator.TwoFactorEnabled = false;
            await identityDb.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthReadiness_RemainsAnonymous()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
        request.Headers.Add("X-Test-Anonymous", "true");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousClient_CannotReadDeviceApi()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/devices");
        request.Headers.Add("X-Test-Anonymous", "true");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HouseholdMember_CannotCreateHousehold()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/households/")
        {
            Content = JsonContent.Create(new { name = "Not permitted" })
        };
        request.Headers.Add("X-Test-Role", "Household Member");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task MfaRecoveryCodes_AreHashedAndSingleUse()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var recoveryCodes = scope.ServiceProvider.GetRequiredService<MfaRecoveryCodeService>();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var generated = await recoveryCodes.GenerateAsync(2);
        Assert.Equal(10, generated.Count);
        Assert.True(await recoveryCodes.RedeemAsync(2, generated[0]));
        Assert.False(await recoveryCodes.RedeemAsync(2, generated[0]));

        var storedCodes = await identityDb.MfaRecoveryCodes
            .Where(code => code.UserId == 2)
            .ToListAsync();
        Assert.Equal(10, storedCodes.Count);
        Assert.DoesNotContain(storedCodes, code => generated.Contains(code.CodeHash));
        Assert.Single(storedCodes, code => code.UsedAtUtc is not null);
    }

    [Fact]
    public async Task UserCannotReadAnotherHouseholdsResources()
    {
        var householdId = await CreateHouseholdAsync("Private Home");
        await _client.PostAsJsonAsync($"/api/devices/{householdId}", ValidRequest());

        using var householdRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/households/{householdId}");
        householdRequest.Headers.Add("X-Test-User-Id", "2");
        var householdResponse = await _client.SendAsync(householdRequest);

        using var devicesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/devices");
        devicesRequest.Headers.Add("X-Test-User-Id", "2");
        var devicesResponse = await _client.SendAsync(devicesRequest);
        var devices = await devicesResponse.Content.ReadFromJsonAsync<DeviceResponse[]>();

        Assert.Equal(HttpStatusCode.NotFound, householdResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, devicesResponse.StatusCode);
        Assert.NotNull(devices);
        Assert.Empty(devices);
    }


    [Fact]
    public async Task HouseholdMember_CanReadButCannotRegisterDevices()
    {
        var householdId = await CreateHouseholdAsync("Shared Home");
        var registration = await _client.PostAsJsonAsync(
            $"/api/devices/{householdId}", ValidRequest());
        var registered = await registration.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.NotNull(registered);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identityDb.HouseholdMemberships.Add(new HouseholdMembership(
                2, householdId, HouseholdRole.HouseholdMember));
            identityDb.Roles.AddRange(
            new IdentityRole<int>("Administrator") { Id = 1, NormalizedName = "ADMINISTRATOR" },
            new IdentityRole<int>("Household Member") { Id = 2, NormalizedName = "HOUSEHOLD MEMBER" },
            new IdentityRole<int>("Read-only Viewer") { Id = 3, NormalizedName = "READ-ONLY VIEWER" });
        identityDb.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = 1, RoleId = 1 },
            new IdentityUserRole<int> { UserId = 2, RoleId = 2 });
        await identityDb.SaveChangesAsync();
        }

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/devices");
        listRequest.Headers.Add("X-Test-User-Id", "2");
        listRequest.Headers.Add("X-Test-Role", "Household Member");
        var listResponse = await _client.SendAsync(listRequest);
        var visibleDevices = await listResponse.Content.ReadFromJsonAsync<DeviceResponse[]>();

        using var registerRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/devices/{householdId}")
        {
            Content = JsonContent.Create(ValidRequest())
        };
        registerRequest.Headers.Add("X-Test-User-Id", "2");
        registerRequest.Headers.Add("X-Test-Role", "Household Member");
        var registerResponse = await _client.SendAsync(registerRequest);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(visibleDevices);
        Assert.Single(visibleDevices);
        Assert.Equal(HttpStatusCode.Forbidden, registerResponse.StatusCode);
    }

    [Fact]
    public async Task DeviceRegistrationPage_RendersWizardForExistingHousehold()
    {
        var householdId = await CreateHouseholdAsync("Wizard Home");

        var response = await _client.GetAsync($"/Devices/Register?householdId={householdId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Register a device", html);
        Assert.Contains("Electrical configuration", html);
        Assert.Contains("Warning thresholds", html);
    }

    [Fact]
    public async Task DeviceDetailsPage_RendersOperationalConsole()
    {
        var householdId = await CreateHouseholdAsync("Console Home");
        var registration = await _client.PostAsJsonAsync($"/api/devices/{householdId}", ValidRequest());
        var device = await registration.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.NotNull(device);

        var response = await _client.GetAsync($"/Devices/{device.DeviceCode}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Device console", html);
        Assert.Contains("Live measurements", html);
        Assert.Contains("Awaiting telemetry", html);
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
        Assert.Equal($"/api/households/{household.Id}", response.Headers.Location?.ToString());
        var getResponse = await _client.GetAsync(response.Headers.Location);
        var retrieved = await getResponse.Content.ReadFromJsonAsync<HouseholdResponse>();
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(household.Id, retrieved?.Id);
    }

    [Fact]
    public async Task SwaggerDocument_IsAvailableInDevelopment()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Energy Management System API", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadinessEndpoint_ReportsDatabaseConnectivity()
    {
        var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
    public async Task ListDevices_AppliesPaginationAndReportsPageMetadata()
    {
        var householdId = await CreateHouseholdAsync("Pagination Home");
        await _client.PostAsJsonAsync($"/api/devices/{householdId}", ValidRequest());
        await _client.PostAsJsonAsync($"/api/devices/{householdId}", ValidRequest());

        var response = await _client.GetAsync("/api/devices?page=2&pageSize=1");
        var devices = await response.Content.ReadFromJsonAsync<DeviceResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(devices);
        Assert.Single(devices);
        Assert.Equal("2", response.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("2", response.Headers.GetValues("X-Page").Single());
        Assert.Equal("1", response.Headers.GetValues("X-Page-Size").Single());
        Assert.Equal("2", response.Headers.GetValues("X-Total-Pages").Single());
    }

    [Fact]
    public async Task ListDevices_RejectsPageSizeAboveMaximum()
    {
        var response = await _client.GetAsync("/api/devices?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync();
        await identityDb.Database.MigrateAsync();
        identityDb.Users.AddRange(
            new ApplicationUser
            {
                Id = 1,
                UserName = "integration-test-admin@example.test",
                Email = "integration-test-admin@example.test",
                EmailConfirmed = true,
                TwoFactorEnabled = true
            },
            new ApplicationUser
            {
                Id = 2,
                UserName = "integration-test-other@example.test",
                Email = "integration-test-other@example.test",
                EmailConfirmed = true
            });
        await identityDb.SaveChangesAsync();
    }

    private static RegisterDeviceRequest ValidRequest() => new(
        "Main Energy Monitor",
        "ESP32",
        "1.0.0",
        "Main Distribution Board",
        ElectricalConfiguration.SinglePhase,
        null,
        new DeviceThresholdsRequest());
}
