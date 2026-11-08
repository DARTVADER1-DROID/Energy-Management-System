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
    public void Rename_TrimsAndReplacesHouseholdName()
    {
        var household = new Household("Original home");

        household.Rename("  Updated home  ");

        Assert.Equal("Updated home", household.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("x")]
    [InlineData("                                                                                                                         ")]
    public void Rename_RejectsInvalidName(string? name)
    {
        var household = new Household("Original home");

        Assert.Throws<ArgumentException>(() => household.Rename(name!));
        Assert.Equal("Original home", household.Name);
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
