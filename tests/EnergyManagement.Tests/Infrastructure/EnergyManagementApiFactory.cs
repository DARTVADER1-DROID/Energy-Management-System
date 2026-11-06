using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnergyManagement.Tests.Infrastructure;

public sealed class EnergyManagementApiFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EnergyManagement"] = connectionString,
                ["EMS_PUBLIC_BASE_URL"] = "https://ems.example.test",
                ["EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
            });
        });

        // Override the application's DbContext registration directly. This keeps
        // database resets and API requests on the exact same test database,
        // independent of configuration-provider ordering in the test host.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<EnergyManagementDbContext>>();
            services.RemoveAll<EnergyManagementDbContext>();

            services.AddDbContext<EnergyManagementDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<TestEmailSender>();
            services.AddSingleton<IEmailSender>(provider =>
                provider.GetRequiredService<TestEmailSender>());

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "IntegrationTest";
                options.DefaultChallengeScheme = "IntegrationTest";
                options.DefaultForbidScheme = "IntegrationTest";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                "IntegrationTest", _ => { });
        });
    }
}
