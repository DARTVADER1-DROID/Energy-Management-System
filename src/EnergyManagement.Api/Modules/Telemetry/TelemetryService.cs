using System.Text.Json;
using EnergyManagement.Api.Infrastructure.Persistence;
using EnergyManagement.Api.Modules.Devices;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnergyManagement.Api.Modules.Telemetry;

public sealed class TelemetryService(
    EnergyManagementDbContext dbContext,
    ILogger<TelemetryService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaximumSensorCount = 16;

    public async Task<TelemetryAcceptedResponse> IngestAsync(
        Device device,
        TelemetryEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        Validate(envelope);

        var receivedAtUtc = DateTimeOffset.UtcNow;
        var payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var reading = new TelemetryReading(
            device.Id,
            envelope.SampleId,
            envelope.SchemaVersion,
            envelope.CapturedAtUtc,
            receivedAtUtc,
            payloadJson);

        dbContext.TelemetryReadings.Add(reading);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            dbContext.Entry(reading).State = EntityState.Detached;
            throw new DuplicateTelemetrySampleException(envelope.SampleId, exception);
        }

        logger.LogInformation(
            "Accepted telemetry sample {SampleId} from device {DeviceCode}.",
            envelope.SampleId, device.DeviceCode);

        return new TelemetryAcceptedResponse(
            envelope.SampleId, device.DeviceCode, envelope.CapturedAtUtc, receivedAtUtc);
    }

    public async Task<IReadOnlyList<TelemetryReadingResponse>> GetForDeviceAsync(
        int deviceId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero ||
            fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(31))
            throw new ArgumentException("The telemetry range must be UTC, ordered, and no longer than 31 days.");

        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var readings = await dbContext.TelemetryReadings
            .AsNoTracking()
            .Where(item => item.DeviceId == deviceId &&
                item.CapturedAtUtc >= fromUtc && item.CapturedAtUtc < toUtc)
            .OrderByDescending(item => item.CapturedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return readings.Select(item => new TelemetryReadingResponse(
            item.SampleId,
            item.CapturedAtUtc,
            item.ReceivedAtUtc,
            item.SchemaVersion,
            JsonSerializer.Deserialize<TelemetryEnvelope>(item.PayloadJson, JsonOptions)!))
            .ToArray();
    }

    private static void Validate(TelemetryEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.SchemaVersion != 1)
            throw new TelemetryValidationException("schemaVersion", "Only telemetry schema version 1 is supported.");
        if (envelope.SampleId == Guid.Empty)
            throw new TelemetryValidationException("sampleId", "A non-empty sample identifier is required.");
        if (envelope.CapturedAtUtc.Offset != TimeSpan.Zero ||
            envelope.CapturedAtUtc < DateTimeOffset.UtcNow.AddDays(-7) ||
            envelope.CapturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            throw new TelemetryValidationException("capturedAtUtc", "The capture timestamp must be UTC and within the accepted clock window.");
        if (envelope.Measurements is null)
            throw new TelemetryValidationException("measurements", "Measurements are required.");

        var m = envelope.Measurements;
        CheckRange(m.VoltageVolts, 0, 1000, "measurements.voltageVolts");
        CheckRange(m.CurrentAmps, 0, 10000, "measurements.currentAmps");
        CheckRange(m.ActivePowerWatts, -10000000, 10000000, "measurements.activePowerWatts");
        CheckRange(m.EnergyWattHours, 0, 1000000000000, "measurements.energyWattHours");
        CheckRange(m.FrequencyHertz, 0, 1000, "measurements.frequencyHertz");
        CheckRange(m.PowerFactor, -1, 1, "measurements.powerFactor");
        CheckRange(m.TemperatureCelsius, -100, 300, "measurements.temperatureCelsius");

        var hasRootMeasurement = m.VoltageVolts.HasValue || m.CurrentAmps.HasValue ||
            m.ActivePowerWatts.HasValue || m.EnergyWattHours.HasValue ||
            m.FrequencyHertz.HasValue || m.PowerFactor.HasValue || m.TemperatureCelsius.HasValue;

        if (envelope.Phases is { Count: > 0 })
        {
            if (envelope.Phases.Count > 3 ||
                envelope.Phases.Keys.Any(key => key is not ("L1" or "L2" or "L3")))
                throw new TelemetryValidationException("phases", "At most L1, L2, and L3 phase readings are supported.");

            foreach (var (phase, value) in envelope.Phases)
            {
                if (value is null)
                    throw new TelemetryValidationException($"phases.{phase}", "Phase measurements cannot be null.");
                CheckRange(value.VoltageVolts, 0, 1000, $"phases.{phase}.voltageVolts");
                CheckRange(value.CurrentAmps, 0, 10000, $"phases.{phase}.currentAmps");
                CheckRange(value.ActivePowerWatts, -10000000, 10000000, $"phases.{phase}.activePowerWatts");
                CheckRange(value.EnergyWattHours, 0, 1000000000000, $"phases.{phase}.energyWattHours");
                CheckRange(value.PowerFactor, -1, 1, $"phases.{phase}.powerFactor");
                hasRootMeasurement = true;
            }
        }

        if (!hasRootMeasurement)
            throw new TelemetryValidationException("measurements", "At least one root or per-phase measurement is required.");

        if (envelope.Sensors is { Count: > MaximumSensorCount })
            throw new TelemetryValidationException("sensors", $"No more than {MaximumSensorCount} additional sensors are allowed.");

        if (envelope.Sensors is not null)
        {
            foreach (var (name, value) in envelope.Sensors)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 40)
                    throw new TelemetryValidationException("sensors", "Sensor names must contain 1 to 40 characters.");
                CheckRange(value, -1000000000000, 1000000000000, $"sensors.{name}");
            }
        }
    }

    private static void CheckRange(decimal? value, decimal minimum, decimal maximum, string field)
    {
        if (value.HasValue && (value.Value < minimum || value.Value > maximum))
            throw new TelemetryValidationException(field, $"Value must be between {minimum} and {maximum}.");
    }
}

public sealed class TelemetryValidationException(string field, string message)
    : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class DuplicateTelemetrySampleException(Guid sampleId, Exception innerException)
    : Exception($"Telemetry sample {sampleId} has already been received.", innerException);
