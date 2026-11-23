using System.ComponentModel.DataAnnotations;
using EnergyManagement.Api.Modules.Devices;
using EnergyManagement.Api.Web.Pages.Admin;
using Xunit;

namespace EnergyManagement.Tests.Domain;

public sealed class AdminInputValidationTests
{
    [Fact]
    public void ClientCreate_AcceptsValidEmailAndMinimumLengthPassword()
    {
        var input = new ClientsModel.CreateClientInput
        {
            Email = "client@example.test",
            Password = "StrongPass1!"
        };

        Assert.Empty(Validate(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("client@")]
    public void ClientCreate_RejectsMalformedEmail(string email)
    {
        var input = new ClientsModel.CreateClientInput
        {
            Email = email,
            Password = "StrongPass1!"
        };

        Assert.NotEmpty(Validate(input));
    }

    [Fact]
    public void ClientCreate_RejectsPasswordShorterThanTwelveCharacters()
    {
        var input = new ClientsModel.CreateClientInput
        {
            Email = "client@example.test",
            Password = "Short1!"
        };

        Assert.Contains(Validate(input), error => error.MemberNames.Contains(nameof(input.Password)));
    }

    [Fact]
    public void ClientEdit_RejectsNonPositiveUserIds()
    {
        var input = new ClientsModel.EditClientInput
        {
            UserId = 0,
            Email = "client@example.test"
        };

        Assert.Contains(Validate(input), error => error.MemberNames.Contains(nameof(input.UserId)));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("  ")]
    [InlineData("")]
    public void HouseholdInput_RejectsNamesShorterThanTwoOrBlank(string name)
    {
        var input = new HouseholdsModel.HouseholdInput { Name = name };

        Assert.NotEmpty(Validate(input));
    }

    [Fact]
    public void HouseholdInput_AcceptsMaximumLengthNameAndTrimsInDomain()
    {
        var input = new HouseholdsModel.HouseholdInput { Name = new string('H', 120) };

        Assert.Empty(Validate(input));
        Assert.Equal(120, new EnergyManagement.Api.Modules.Households.Household(input.Name).Name.Length);
    }

    [Fact]
    public void HouseholdInput_RejectsNameOverMaximumLength()
    {
        var input = new HouseholdsModel.HouseholdInput { Name = new string('H', 121) };

        Assert.NotEmpty(Validate(input));
    }

    [Fact]
    public void DeviceInput_AcceptsValidSinglePhaseConfiguration()
    {
        var input = ValidDeviceInput();

        Assert.Empty(Validate(input));
    }

    [Fact]
    public void DeviceInput_RejectsUnsupportedElectricalConfiguration()
    {
        var input = ValidDeviceInput();
        input.ElectricalConfiguration = (ElectricalConfiguration)int.MaxValue;

        Assert.Contains(Validate(input),
            error => error.MemberNames.Contains(nameof(input.ElectricalConfiguration)));
    }

    [Theory]
    [InlineData(121, "Name")]
    [InlineData(81, "HardwareModel")]
    [InlineData(81, "FirmwareVersion")]
    [InlineData(161, "InstallationLocation")]
    [InlineData(1001, "Description")]
    public void DeviceInput_RejectsMetadataOverMaximumLength(int length, string field)
    {
        var input = ValidDeviceInput();
        var value = new string('X', length);
        switch (field)
        {
            case "Name": input.Name = value; break;
            case "HardwareModel": input.HardwareModel = value; break;
            case "FirmwareVersion": input.FirmwareVersion = value; break;
            case "InstallationLocation": input.InstallationLocation = value; break;
            case "Description": input.Description = value; break;
        }

        Assert.Contains(Validate(input), error => error.MemberNames.Contains(field));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100000.01")]
    public void DeviceInput_RejectsVoltageThresholdOutsideRange(string rawVoltage)
    {
        var voltage = decimal.Parse(rawVoltage, System.Globalization.CultureInfo.InvariantCulture);
        var input = ValidDeviceInput();
        input.MaximumVoltage = voltage;

        Assert.Contains(Validate(input),
            error => error.MemberNames.Contains(nameof(input.MaximumVoltage)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86401)]
    public void DeviceInput_RejectsWarningPersistenceOutsideRange(int seconds)
    {
        var input = ValidDeviceInput();
        input.WarningPersistenceSeconds = seconds;

        Assert.Contains(Validate(input),
            error => error.MemberNames.Contains(nameof(input.WarningPersistenceSeconds)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(86400)]
    public void DeviceInput_AcceptsWarningPersistenceAtBothBounds(int seconds)
    {
        var input = ValidDeviceInput();
        input.MaximumCurrent = 10;
        input.WarningPersistenceSeconds = seconds;

        Assert.Empty(Validate(input));
    }

    [Fact]
    public void DeviceInput_RejectsMissingHousehold()
    {
        var input = ValidDeviceInput();
        input.HouseholdId = 0;

        Assert.Contains(Validate(input),
            error => error.MemberNames.Contains(nameof(input.HouseholdId)));
    }

    private static DevicesModel.DeviceInput ValidDeviceInput() => new()
    {
        HouseholdId = 1,
        Name = "Monitor",
        HardwareModel = "ESP32",
        FirmwareVersion = "1.0.0",
        InstallationLocation = "Main DB",
        ElectricalConfiguration = ElectricalConfiguration.SinglePhase
    };

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
