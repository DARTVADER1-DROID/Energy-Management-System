using EnergyManagement.Api.Infrastructure.IdentityPersistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnergyManagement.Api.Infrastructure.IdentityPersistence.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("202611070001_DisableMfaForAllUsers")]
public sealed class DisableMfaForAllUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE users
            SET "TwoFactorEnabled" = FALSE
            WHERE "TwoFactorEnabled" = TRUE;
            """);
    }

    // Disabling MFA is an intentional security-policy change. Rolling back this
    // migration must not silently re-enable MFA for every account.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
