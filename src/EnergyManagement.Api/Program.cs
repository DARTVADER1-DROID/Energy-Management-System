using System.Security.Claims;
using EnergyManagement.Api.Endpoints;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Modules.Identity;
using EnergyManagement.Api.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EnergyManagement")
    ?? Environment.GetEnvironmentVariable("EMS_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Configure ConnectionStrings:EnergyManagement before starting the API.");

builder.Services.AddDbContext<EnergyManagementDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__IdentityMigrationsHistory")));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole<int>>()
    .AddEntityFrameworkStores<IdentityDbContext>()
    .AddDefaultTokenProviders()
    .AddSignInManager();

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-EMS.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.Events.OnSigningIn = context =>
    {
        var identity = context.Principal?.Identity as ClaimsIdentity;
        if (identity is not null)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            if (!identity.HasClaim(claim => claim.Type == "ems_session_started"))
                identity.AddClaim(new Claim("ems_session_started", now));

            var previousActivity = identity.FindFirst("ems_last_activity");
            if (previousActivity is not null)
                identity.RemoveClaim(previousActivity);
            identity.AddClaim(new Claim("ems_last_activity", now));
        }

        return Task.CompletedTask;
    };

    options.Events.OnValidatePrincipal = context =>
    {
        var now = DateTimeOffset.UtcNow;
        var startedClaim = context.Principal?.FindFirst("ems_session_started")?.Value;
        var activityClaim = context.Principal?.FindFirst("ems_last_activity")?.Value;

        if (!DateTimeOffset.TryParse(startedClaim, out var startedAt)
            || !DateTimeOffset.TryParse(activityClaim, out var lastActivityAt)
            || now - startedAt > TimeSpan.FromHours(8)
            || now - lastActivityAt > TimeSpan.FromMinutes(15))
        {
            context.RejectPrincipal();
            return context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        if (context.Principal?.Identity is ClaimsIdentity validatedIdentity)
        {
            validatedIdentity.RemoveClaim(validatedIdentity.FindFirst("ems_last_activity"));
            validatedIdentity.AddClaim(new Claim("ems_last_activity", now.ToString("O")));
            context.ShouldRenew = true;
        }

        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("HouseholdAdministrator", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Administrator"));
    options.AddPolicy("HouseholdOperator", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Administrator", "Household Member"));
});

builder.Services.AddScoped<DeviceRegistrationService>();
builder.Services.AddScoped<DashboardReadService>();
builder.Services.AddScoped<HouseholdAccessService>();
builder.Services.AddScoped<InitialAdminBootstrapper>();
builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/Web/Pages";
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
});

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

// Identity uses its own migration history so its schema can evolve independently
// from the existing EMS domain schema.
await using (var scope = app.Services.CreateAsyncScope())
{
    var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await identityDb.Database.MigrateAsync();

    var bootstrapper = scope.ServiceProvider.GetRequiredService<InitialAdminBootstrapper>();
    await bootstrapper.InitializeAsync();
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

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthEndpoints();
app.MapDeviceEndpoints();
app.MapRazorPages();

app.Run();

// Exposed for integration tests using WebApplicationFactory.
public partial class Program
{
}
