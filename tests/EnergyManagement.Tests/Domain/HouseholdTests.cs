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
    public void Rename_TrimsAndUpdatesHouseholdName()
    {
        var household = new Household("Old name");

        household.Rename("  New name  ");

        Assert.Equal("New name", household.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_RejectsBlankName(string name)
    {
        var household = new Household("My Home");

        Assert.Throws<ArgumentException>(() => household.Rename(name));
    }

    [Fact]
    public void Rename_RejectsNameLongerThan120Characters()
    {
        var household = new Household("My Home");

        Assert.Throws<ArgumentException>(() => household.Rename(new string('x', 121)));
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
