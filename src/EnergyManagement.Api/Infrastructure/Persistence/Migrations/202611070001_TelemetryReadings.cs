using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnergyManagement.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EnergyManagementDbContext))]
[Migration("202611070001_TelemetryReadings")]
public sealed class TelemetryReadings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "telemetry_readings",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DeviceId = table.Column<int>(type: "integer", nullable: false),
                SampleId = table.Column<Guid>(type: "uuid", nullable: false),
                SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PayloadJson = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_telemetry_readings", x => x.Id);
                table.ForeignKey(
                    name: "FK_telemetry_readings_devices_DeviceId",
                    column: x => x.DeviceId,
                    principalTable: "devices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_telemetry_readings_DeviceId_SampleId",
            table: "telemetry_readings",
            columns: new[] { "DeviceId", "SampleId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_telemetry_readings_DeviceId_CapturedAtUtc",
            table: "telemetry_readings",
            columns: new[] { "DeviceId", "CapturedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "telemetry_readings");
    }
}
