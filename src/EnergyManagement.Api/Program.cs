using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
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
using Microsoft.Extensions.Options;

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

builder.Services.AddDbContext<DeviceSecurityDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__DeviceSecurityMigrationsHistory")));

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

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromMinutes(30));
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

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
            var currentPrincipal = context.HttpContext.User;
            var currentSessionStarted = currentPrincipal.FindFirst("ems_session_started")?.Value;
            var currentRecentAuth = currentPrincipal.FindFirst("ems_recent_auth")?.Value;
            var currentUserIsAuthenticated = currentPrincipal.Identity?.IsAuthenticated == true;
            var recentAuthenticationWasVerified =
                context.HttpContext.Items.ContainsKey("ems_recent_auth_verified");

            var oldSessionClaim = identity.FindFirst("ems_session_started");
            if (oldSessionClaim is not null)
                identity.RemoveClaim(oldSessionClaim);
            identity.AddClaim(new Claim("ems_session_started", currentSessionStarted ?? now));

            var oldRecentClaim = identity.FindFirst("ems_recent_auth");
            if (oldRecentClaim is not null)
                identity.RemoveClaim(oldRecentClaim);

            if (recentAuthenticationWasVerified || !currentUserIsAuthenticated)
                identity.AddClaim(new Claim("ems_recent_auth", now));
            else if (currentRecentAuth is not null)
                identity.AddClaim(new Claim("ems_recent_auth", currentRecentAuth));

            var previousActivity = identity.FindFirst("ems_last_activity");
            if (previousActivity is not null)
                identity.RemoveClaim(previousActivity);
            identity.AddClaim(new Claim("ems_last_activity", now));
        }

        return Task.CompletedTask;
    };

    options.Events.OnValidatePrincipal = async context =>
    {
        var now = DateTimeOffset.UtcNow;
        var principal = context.Principal;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var identityOptions = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<IdentityOptions>>().Value;
        var stampClaim = principal?.FindFirstValue(
            identityOptions.ClaimsIdentity.SecurityStampClaimType);
        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();

        // The application cookie event replaces Identity's default validator.
        // Check the persisted stamp here so MFA resets and role changes revoke
        // existing cookies immediately rather than waiting for a sliding interval.
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        var currentStamp = user is null ? null : await userManager.GetSecurityStampAsync(user);
        if (user is null || string.IsNullOrWhiteSpace(stampClaim) ||
            !string.Equals(stampClaim, currentStamp, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        var startedClaim = principal?.FindFirst("ems_session_started")?.Value;
        var activityClaim = principal?.FindFirst("ems_last_activity")?.Value;
        if (!DateTimeOffset.TryParse(startedClaim, out var startedAt)
            || !DateTimeOffset.TryParse(activityClaim, out var lastActivityAt)
            || now - startedAt > TimeSpan.FromHours(8)
            || now - lastActivityAt > TimeSpan.FromMinutes(15))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        if (principal?.Identity is ClaimsIdentity validatedIdentity)
        {
            var previousActivity = validatedIdentity.FindFirst("ems_last_activity");
            if (previousActivity is not null)
                validatedIdentity.RemoveClaim(previousActivity);
            validatedIdentity.AddClaim(new Claim("ems_last_activity", now.ToString("O")));
            context.ShouldRenew = true;
        }
    };
});

builder.Services.AddScoped<IAuthorizationHandler, AdministratorMfaAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    var authenticatedWithAdministratorMfa = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new AdministratorMfaRequirement())
        .Build();

    options.DefaultPolicy = authenticatedWithAdministratorMfa;
    options.FallbackPolicy = authenticatedWithAdministratorMfa;

    options.AddPolicy("HouseholdAdministrator", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Administrator"));
    options.AddPolicy("HouseholdOperator", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Administrator", "Household Member"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value ?? "/";
        if (!path.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetNoLimiter("non-account");

        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{client}:{path}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("device-hmac", context =>
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var deviceCode = context.Request.Headers["X-EMS-Device"].ToString();
        return RateLimitPartition.GetFixedWindowLimiter(
            $"device-hmac:{clientIp}:{deviceCode}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("device-provisioning", context =>
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"device-provisioning:{clientIp}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddScoped<DeviceRegistrationService>();
builder.Services.AddScoped<DeviceAuthenticationService>();
builder.Services.AddScoped<DashboardReadService>();
builder.Services.AddScoped<HouseholdAccessService>();
builder.Services.AddScoped<InitialAdminBootstrapper>();
builder.Services.AddScoped<MfaRecoveryCodeService>();
builder.Services.AddScoped<HouseholdInvitationService>();
builder.Services.AddScoped<MfaAdministrativeRecoveryService>();
builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/Web/Pages";
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Account/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/Account/ResetPassword");
    options.Conventions.AllowAnonymousToPage("/Account/EnableMfa");
    options.Conventions.AllowAnonymousToPage("/Account/TwoFactorLogin");
    options.Conventions.AllowAnonymousToPage("/Account/AcceptInvitation");
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

await using (var scope = app.Services.CreateAsyncScope())
{
    var energyDb = scope.ServiceProvider.GetRequiredService<EnergyManagementDbContext>();
    await energyDb.Database.MigrateAsync();

    var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await identityDb.Database.MigrateAsync();

    var deviceSecurityDb = scope.ServiceProvider.GetRequiredService<DeviceSecurityDbContext>();
    await deviceSecurityDb.Database.MigrateAsync();

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
app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseStaticFiles();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Account") ||
        context.Request.Path.StartsWithSegments("/Households/Users"))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
    }

    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthEndpoints();
app.MapDeviceEndpoints();
app.MapRazorPages();

app.Run();

public partial class Program
{
}
