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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Energy Management System API",
        Version = "v1",
        Description = "HTTP API for residential energy management."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Energy Management System API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapHealthEndpoints();
app.MapDeviceEndpoints();

app.Run();

// Exposed for future integration tests using WebApplicationFactory.
public partial class Program
{
}
