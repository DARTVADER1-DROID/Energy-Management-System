using EnergyManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EnergyManagement.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DeviceSecurityDbContext))]
[Migration("202611080001_PurgeSoftRemovedDevices")]
public sealed class PurgeSoftRemovedDevices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Earlier releases soft-removed devices. Convert those records to the
        // new permanent-deletion policy during deployment. Telemetry is removed
        // by the devices -> telemetry_readings ON DELETE CASCADE relationship.
        // Security audit events are intentionally retained for accountability.
        migrationBuilder.Sql("""
            INSERT INTO device_security_audit_events
                ("DeviceId", "ActorUserId", "EventType", "Details", "CreatedAtUtc")
            SELECT "Id", NULL, 'DevicePermanentlyDeleted',
                'Legacy soft-removed device permanently purged during migration.', NOW()
            FROM devices WHERE "IsActive" = FALSE;

            DELETE FROM device_credentials
            WHERE "DeviceId" IN (SELECT "Id" FROM devices WHERE "IsActive" = FALSE);

            DELETE FROM device_provisioning_codes
            WHERE "DeviceId" IN (SELECT "Id" FROM devices WHERE "IsActive" = FALSE);

            DELETE FROM device_request_nonces
            WHERE "DeviceId" IN (SELECT "Id" FROM devices WHERE "IsActive" = FALSE);

            DELETE FROM devices WHERE "IsActive" = FALSE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Permanent deletion cannot be reversed; removed device and telemetry
        // records are intentionally not reconstructed.
    }
}
