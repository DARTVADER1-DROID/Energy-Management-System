using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnergyManagement.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DeviceSecurityDbContext))]
[Migration("202611070001_InitialDeviceSecurity")]
public sealed class InitialDeviceSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "device_provisioning_codes",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DeviceId = table.Column<int>(type: "integer", nullable: false),
                CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_device_provisioning_codes", x => x.Id));

        migrationBuilder.CreateTable(
            name: "device_credentials",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DeviceId = table.Column<int>(type: "integer", nullable: false),
                SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProtectedSigningKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IsPendingRotation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                RotationExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_device_credentials", x => x.Id));

        migrationBuilder.CreateTable(
            name: "device_request_nonces",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DeviceId = table.Column<int>(type: "integer", nullable: false),
                Nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_device_request_nonces", x => x.Id));

        migrationBuilder.CreateIndex("IX_device_provisioning_codes_CodeHash", "device_provisioning_codes", "CodeHash", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_device_provisioning_codes_DeviceId",
            table: "device_provisioning_codes",
            column: "DeviceId",
            unique: true,
            filter: "\"UsedAtUtc\" IS NULL AND \"RevokedAtUtc\" IS NULL");
        migrationBuilder.CreateIndex("IX_device_provisioning_codes_DeviceId_ExpiresAtUtc", "device_provisioning_codes", new[] { "DeviceId", "ExpiresAtUtc" });
        migrationBuilder.CreateIndex("IX_device_credentials_DeviceId_RevokedAtUtc", "device_credentials", new[] { "DeviceId", "RevokedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_device_credentials_DeviceId",
            table: "device_credentials",
            column: "DeviceId",
            unique: true,
            filter: "\"IsPendingRotation\" = TRUE AND \"RevokedAtUtc\" IS NULL");
        migrationBuilder.CreateIndex("IX_device_request_nonces_DeviceId_Nonce", "device_request_nonces", new[] { "DeviceId", "Nonce" }, unique: true);
        migrationBuilder.CreateIndex("IX_device_request_nonces_CreatedAtUtc", "device_request_nonces", "CreatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("device_request_nonces");
        migrationBuilder.DropTable("device_credentials");
        migrationBuilder.DropTable("device_provisioning_codes");
    }
}
