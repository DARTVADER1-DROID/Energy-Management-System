namespace EnergyManagement.Api.Modules.Households;

public sealed class Household
{
    private Household()
    {
    }

    public Household(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A household name is required.", nameof(name));
        }

        Name = name.Trim();
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int NextDeviceNumber { get; private set; } = 1;

    public string HouseholdCode => $"H{Id:000}";

    public int AllocateDeviceNumber()
    {
        var allocatedNumber = NextDeviceNumber;
        NextDeviceNumber++;
        return allocatedNumber;
    }
}
