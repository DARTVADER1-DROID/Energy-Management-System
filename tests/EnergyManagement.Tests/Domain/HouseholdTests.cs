using EnergyManagement.Api.Modules.Households;
using Xunit;

namespace EnergyManagement.Tests.Domain;

public sealed class HouseholdTests
{
    [Fact]
    public void Constructor_TrimsHouseholdName()
    {
        var household = new Household("  My Home  ");

        Assert.Equal("My Home", household.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => new Household(name));
    }

    [Fact]
    public void AllocateDeviceNumber_ReturnsIncreasingNumbers()
    {
        var household = new Household("My Home");

        Assert.Equal(1, household.AllocateDeviceNumber());
        Assert.Equal(2, household.AllocateDeviceNumber());
        Assert.Equal(3, household.AllocateDeviceNumber());
    }
}
