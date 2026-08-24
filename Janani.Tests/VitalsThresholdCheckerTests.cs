using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class VitalsThresholdCheckerTests
{
    private static VitalsReading Normal() => new()
    {
        SystolicBp = 120,
        DiastolicBp = 80,
        HeartRate = 75,
        TemperatureC = 36.8m,
        OxygenSaturation = 97
    };

    [Fact]
    public void AllNormalReadings_ProduceNoConcerns()
    {
        var concerns = VitalsThresholdChecker.Check(Normal());
        Assert.Empty(concerns);
    }

    [Theory]
    [InlineData(181, AlertSeverity.Critical)]
    [InlineData(180, null)] // boundary: exactly 180 is in range
    [InlineData(89, AlertSeverity.Critical)]
    [InlineData(90, null)] // boundary: exactly 90 is in range
    public void SystolicBp_OutOfRange_FlagsCritical(int systolic, AlertSeverity? expected)
    {
        var reading = Normal();
        reading.SystolicBp = systolic;
        var concerns = VitalsThresholdChecker.Check(reading);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("Systolic"));
        else
            Assert.Contains(concerns, c => c.Description.Contains("Systolic") && c.Severity == expected);
    }

    [Theory]
    [InlineData(121, AlertSeverity.Critical)]
    [InlineData(59, AlertSeverity.Critical)]
    public void DiastolicBp_OutOfRange_FlagsCritical(int diastolic, AlertSeverity expected)
    {
        var reading = Normal();
        reading.DiastolicBp = diastolic;
        var concerns = VitalsThresholdChecker.Check(reading);
        Assert.Contains(concerns, c => c.Description.Contains("Diastolic") && c.Severity == expected);
    }

    [Theory]
    [InlineData(121, AlertSeverity.Warning)]
    [InlineData(49, AlertSeverity.Warning)]
    public void HeartRate_OutOfRange_FlagsWarning(int heartRate, AlertSeverity expected)
    {
        var reading = Normal();
        reading.HeartRate = heartRate;
        var concerns = VitalsThresholdChecker.Check(reading);
        Assert.Contains(concerns, c => c.Description.Contains("Heart rate") && c.Severity == expected);
    }

    [Theory]
    [InlineData(38.6, AlertSeverity.Warning)]
    [InlineData(34.9, AlertSeverity.Warning)]
    public void Temperature_OutOfRange_FlagsWarning(double temp, AlertSeverity expected)
    {
        var reading = Normal();
        reading.TemperatureC = (decimal)temp;
        var concerns = VitalsThresholdChecker.Check(reading);
        Assert.Contains(concerns, c => c.Description.Contains("Temperature") && c.Severity == expected);
    }

    [Fact]
    public void OxygenSaturation_Below92_FlagsCritical()
    {
        var reading = Normal();
        reading.OxygenSaturation = 91;
        var concerns = VitalsThresholdChecker.Check(reading);
        Assert.Contains(concerns, c => c.Description.Contains("Oxygen") && c.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public void OxygenSaturation_At92_IsNotFlagged()
    {
        var reading = Normal();
        reading.OxygenSaturation = 92;
        var concerns = VitalsThresholdChecker.Check(reading);
        Assert.DoesNotContain(concerns, c => c.Description.Contains("Oxygen"));
    }

    [Fact]
    public void MultipleOutOfRangeValues_FlagsEachIndependently()
    {
        var reading = Normal();
        reading.SystolicBp = 200;
        reading.OxygenSaturation = 85;
        var concerns = VitalsThresholdChecker.Check(reading);
        Assert.Equal(2, concerns.Count);
    }

    [Fact]
    public void NullValues_AreNotFlagged()
    {
        var reading = new VitalsReading(); // all fields null
        var concerns = VitalsThresholdChecker.Check(reading);
        Assert.Empty(concerns);
    }
}
