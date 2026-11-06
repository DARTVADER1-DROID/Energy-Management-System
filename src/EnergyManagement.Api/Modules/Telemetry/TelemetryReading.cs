namespace EnergyManagement.Api.Modules.Telemetry;

public sealed class TelemetryReading
{
    private TelemetryReading()
    {
    }

    public TelemetryReading(
        int deviceId,
        Guid sampleId,
        int schemaVersion,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset receivedAtUtc,
        string payloadJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceId);
        if (sampleId == Guid.Empty)
            throw new ArgumentException("A sample identifier is required.", nameof(sampleId));
        if (schemaVersion != 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (capturedAtUtc.Offset != TimeSpan.Zero || receivedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Telemetry timestamps must be UTC.");
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        DeviceId = deviceId;
        SampleId = sampleId;
        SchemaVersion = schemaVersion;
        CapturedAtUtc = capturedAtUtc;
        ReceivedAtUtc = receivedAtUtc;
        PayloadJson = payloadJson;
    }

    public long Id { get; private set; }
    public int DeviceId { get; private set; }
    public Guid SampleId { get; private set; }
    public int SchemaVersion { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
}
