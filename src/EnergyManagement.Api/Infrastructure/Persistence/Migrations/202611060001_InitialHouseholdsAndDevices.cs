using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnergyManagement.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EnergyManagementDbContext))]
[Migration("202611060001_InitialHouseholdsAndDevices")]
public sealed class InitialHouseholdsAndDevices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "households",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                NextDeviceNumber = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_households", x => x.Id));

        migrationBuilder.CreateTable(
            name: "devices",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                HouseholdId = table.Column<int>(type: "integer", nullable: false),
                DeviceCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DeviceNumber = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                HardwareModel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                FirmwareVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                InstallationLocation = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                ElectricalConfiguration = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                minimum_voltage = table.Column<decimal>(type: "numeric", nullable: true),
                maximum_voltage = table.Column<decimal>(type: "numeric", nullable: true),
                maximum_current = table.Column<decimal>(type: "numeric", nullable: true),
                maximum_instantaneous_power = table.Column<decimal>(type: "numeric", nullable: true),
                maximum_daily_energy = table.Column<decimal>(type: "numeric", nullable: true),
                maximum_temperature = table.Column<decimal>(type: "numeric", nullable: true),
                warning_persistence_seconds = table.Column<int>(type: "integer", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_devices", x => x.Id);
                table.ForeignKey(
                    name: "FK_devices_households_HouseholdId",
                    column: x => x.HouseholdId,
                    principalTable: "households",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_devices_DeviceCode", "devices", "DeviceCode", unique: true);
        migrationBuilder.CreateIndex("IX_devices_HouseholdId_DeviceNumber", "devices", new[] { "HouseholdId", "DeviceNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("devices");
        migrationBuilder.DropTable("households");
    }
}
