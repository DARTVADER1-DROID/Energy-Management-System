using EnergyManagement.Api.Endpoints;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EnergyManagement")
    ?? Environment.GetEnvironmentVariable("EMS_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Configure ConnectionStrings:EnergyManagement before starting the API.");

builder.Services.AddDbContext<EnergyManagementDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<DeviceRegistrationService>();
builder.Services.AddScoped<DashboardReadService>();
builder.Services.AddRazorPages(options => options.RootDirectory = "/Web/Pages");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
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

app.UseStaticFiles();
app.MapHealthEndpoints();
app.MapDeviceEndpoints();
app.MapRazorPages();

app.Run();

// Exposed for future integration tests using WebApplicationFactory.
public partial class Program
{
}
