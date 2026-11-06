using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnergyManagement.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DeviceSecurityDbContext))]
[Migration("202611070002_DeviceSecurityAuditEvents")]
public sealed class DeviceSecurityAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "device_security_audit_events",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DeviceId = table.Column<int>(type: "integer", nullable: false),
                ActorUserId = table.Column<int>(type: "integer", nullable: true),
                EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Details = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_device_security_audit_events", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_device_security_audit_events_DeviceId_CreatedAtUtc",
            table: "device_security_audit_events",
            columns: new[] { "DeviceId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("device_security_audit_events");
    }
}
