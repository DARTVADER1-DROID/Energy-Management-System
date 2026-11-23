namespace EnergyManagement.Api.Modules.Households;

public sealed class Household
{
    private Household()
    {
    }

    public Household(string name)
    {
        Name = RequiredName(name);
    }

    public int Id { get; private set; }

    internal static Household Rehydrate(int id, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        return new Household(name) { Id = id };
    }

    public string Name { get; private set; } = string.Empty;

    public int NextDeviceNumber { get; private set; } = 1;

    public string HouseholdCode => $"H{Id:000}";

    public void Rename(string name) => Name = RequiredName(name);

    public int AllocateDeviceNumber()
    {
        var allocatedNumber = NextDeviceNumber;
        NextDeviceNumber++;
        return allocatedNumber;
    }

    private static string RequiredName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("A household name is required and must not exceed 120 characters.", nameof(name));
        return name.Trim();
    }
}
