using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class PregnancyVitalsThresholdCheckerTests
{
    private static PregnancyVitalsReading Normal() => new()
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
        var concerns = PregnancyVitalsThresholdChecker.Check(Normal());
        Assert.Empty(concerns);
    }

    [Theory]
    [InlineData(139, null)]      // boundary: below Warning
    [InlineData(140, AlertSeverity.Warning)]  // Warning tier: gestational hypertension / pre-eclampsia range
    [InlineData(159, AlertSeverity.Warning)]
    [InlineData(160, AlertSeverity.Critical)] // Critical tier: severe range
    public void SystolicBp_TwoTierPreeclampsiaRange_FlagsCorrectSeverity(int systolic, AlertSeverity? expected)
    {
        var reading = Normal();
        reading.SystolicBp = systolic;
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("Blood pressure"));
        else
            Assert.Contains(concerns, c => c.Description.Contains("Blood pressure") && c.Severity == expected);
    }

    [Theory]
    [InlineData(89, null)]
    [InlineData(90, AlertSeverity.Warning)]
    [InlineData(109, AlertSeverity.Warning)]
    [InlineData(110, AlertSeverity.Critical)]
    public void DiastolicBp_TwoTierPreeclampsiaRange_FlagsCorrectSeverity(int diastolic, AlertSeverity? expected)
    {
        var reading = Normal();
        reading.DiastolicBp = diastolic;
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("Blood pressure"));
        else
            Assert.Contains(concerns, c => c.Description.Contains("Blood pressure") && c.Severity == expected);
    }

    [Theory]
    [InlineData(121, AlertSeverity.Warning)]
    [InlineData(49, AlertSeverity.Warning)]
    public void HeartRate_OutOfRange_FlagsWarning(int heartRate, AlertSeverity expected)
    {
        var reading = Normal();
        reading.HeartRate = heartRate;
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);
        Assert.Contains(concerns, c => c.Description.Contains("Heart rate") && c.Severity == expected);
    }

    [Theory]
    [InlineData(37.9, null)]
    [InlineData(38.0, AlertSeverity.Warning)]
    [InlineData(38.9, AlertSeverity.Warning)]
    [InlineData(39.0, AlertSeverity.Critical)]
    public void Temperature_TwoTierFeverRange_FlagsCorrectSeverity(double temp, AlertSeverity? expected)
    {
        var reading = Normal();
        reading.TemperatureC = (decimal)temp;
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);

        if (expected == null)
            Assert.DoesNotContain(concerns, c => c.Description.Contains("Temperature"));
        else
            Assert.Contains(concerns, c => c.Description.Contains("Temperature") && c.Severity == expected);
    }

    [Fact]
    public void OxygenSaturation_Below92_FlagsCritical()
    {
        var reading = Normal();
        reading.OxygenSaturation = 91;
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);
        Assert.Contains(concerns, c => c.Description.Contains("Oxygen") && c.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public void OxygenSaturation_At92_IsNotFlagged()
    {
        var reading = Normal();
        reading.OxygenSaturation = 92;
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);
        Assert.DoesNotContain(concerns, c => c.Description.Contains("Oxygen"));
    }

    [Fact]
    public void MultipleOutOfRangeValues_FlagsEachIndependently()
    {
        var reading = Normal();
        reading.SystolicBp = 165;
        reading.OxygenSaturation = 85;
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);
        Assert.Equal(2, concerns.Count);
    }

    [Fact]
    public void NullValues_AreNotFlagged()
    {
        var reading = new PregnancyVitalsReading(); // all fields null
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);
        Assert.Empty(concerns);
    }
}
