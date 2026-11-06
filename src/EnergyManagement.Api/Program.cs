using EnergyManagement.Api.Endpoints;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EnergyManagement")
    ?? "Host=localhost;Port=5432;Database=energy_management;Username=postgres;Password=postgres";

builder.Services.AddDbContext<EnergyManagementDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<DeviceRegistrationService>();

var app = builder.Build();

app.MapHealthEndpoints();
app.MapDeviceEndpoints();

app.Run();

// Exposed for future integration tests using WebApplicationFactory.
public partial class Program
{
}
