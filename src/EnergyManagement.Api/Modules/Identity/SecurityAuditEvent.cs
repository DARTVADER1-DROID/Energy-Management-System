namespace EnergyManagement.Api.Modules.Identity;

public sealed class SecurityAuditEvent
{
    private SecurityAuditEvent() { }

    public SecurityAuditEvent(int householdId, int actorUserId, int targetUserId, string eventType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(householdId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorUserId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        HouseholdId = householdId;
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        EventType = eventType;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public long Id { get; private set; }
    public int HouseholdId { get; private set; }
    public int ActorUserId { get; private set; }
    public int TargetUserId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
