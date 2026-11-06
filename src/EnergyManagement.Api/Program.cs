using EnergyManagement.Api.Endpoints;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var configuredConnectionString = builder.Configuration.GetConnectionString("EnergyManagement")
    ?? Environment.GetEnvironmentVariable("EMS_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Configure ConnectionStrings:EnergyManagement before starting the API.");

var connectionString = NormalizePostgresConnectionString(configuredConnectionString);

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

// Staging enables this explicitly so schema updates are applied before readiness checks.
// Keep this disabled by default; production migrations should use a controlled release process.
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
    await dbContext.Database.MigrateAsync();
}

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

// Render supplies PostgreSQL URLs (postgres:// or postgresql://), while Npgsql
// expects a keyword/value connection string. Accept both formats so local
// development and Render deployments can share the same configuration key.
static string NormalizePostgresConnectionString(string connectionString)
{
    if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var databaseUri)
        || (databaseUri.Scheme != "postgres" && databaseUri.Scheme != "postgresql"))
    {
        return connectionString;
    }

    var userInfo = databaseUri.UserInfo.Split(':', 2);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = databaseUri.Host,
        Port = databaseUri.IsDefaultPort || databaseUri.Port <= 0 ? 5432 : databaseUri.Port,
        Database = Uri.UnescapeDataString(databaseUri.AbsolutePath.TrimStart('/')),
        Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty
    };

    return builder.ConnectionString;
}

// Exposed for future integration tests using WebApplicationFactory.
public partial class Program
{
}
