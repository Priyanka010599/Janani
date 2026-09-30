using System.Text.Json;
using Janani.Models;
using Janani.Services;
using Xunit;

namespace Janani.Tests;

// Pins the sourced parts of FhirExportService.cs -- the LOINC codes, the profile
// URLs and the honest gaps (Bundle.type, no performer) -- to what its header claims.
public class FhirExportServiceTests
{
    // ── ABHA validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("12345678901234", true)]   // 14 digits
    [InlineData("ravi.kumar@abdm", true)]
    [InlineData("ravi.kumar@sbx", true)]
    [InlineData("1234567890123", false)]   // 13 digits
    [InlineData("123456789012345", false)] // 15 digits
    [InlineData("not-a-number", false)]
    [InlineData("@abdm", false)]           // nothing before the @
    public void IsValidAbhaId_MatchesTheTwoDocumentedForms(string? id, bool expected) =>
        Assert.Equal(expected, FhirExportService.IsValidAbhaId(id));

    // ── Elder vitals bundle ──────────────────────────────────────────────

    [Fact]
    public void ElderVitalsBundle_IsACollectionBundle_NotADocument()
    {
        var elder = new ElderProfile { Id = 1, Name = "Lakshmi", Age = 68, Relation = "Mother" };
        var json = FhirExportService.BuildElderVitalsBundle(elder, []);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Bundle", root.GetProperty("resourceType").GetString());
        // Deliberately not "document" -- see the file header: no Composition/IN-PS
        // narrative is built here, so claiming the document type would overclaim.
        Assert.Equal("collection", root.GetProperty("type").GetString());
    }

    [Fact]
    public void ElderVitalsBundle_UsesTheSourcedLoincCodes()
    {
        var elder = new ElderProfile { Id = 1, Name = "Lakshmi", Age = 68, Relation = "Mother" };
        var reading = new VitalsReading
        {
            Id = 5, ElderProfileId = 1, RecordedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            SystolicBp = 192, DiastolicBp = 104, HeartRate = 140, TemperatureC = 39.5m, OxygenSaturation = 85
        };
        var json = FhirExportService.BuildElderVitalsBundle(elder, [reading]);
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entry").EnumerateArray().ToList();

        var observations = entries
            .Select(e => e.GetProperty("resource"))
            .Where(r => r.GetProperty("resourceType").GetString() == "Observation")
            .ToList();
        Assert.Equal(5, observations.Count); // all five vitals present

        string LoincOf(string display) => observations
            .Single(o => o.GetProperty("code").GetProperty("text").GetString() == display)
            .GetProperty("code").GetProperty("coding")[0].GetProperty("code").GetString()!;

        Assert.Equal("8480-6", LoincOf("Systolic blood pressure"));
        Assert.Equal("8462-4", LoincOf("Diastolic blood pressure"));
        Assert.Equal("8867-4", LoincOf("Heart rate"));
        Assert.Equal("8310-5", LoincOf("Body temperature"));
        Assert.Equal("59408-5", LoincOf("Oxygen saturation, pulse oximetry"));

        foreach (var o in observations)
            Assert.Equal("http://loinc.org", o.GetProperty("code").GetProperty("coding")[0].GetProperty("system").GetString());
    }

    [Fact]
    public void ElderVitalsBundle_SkipsNullReadings_RatherThanFabricatingAValue()
    {
        var elder = new ElderProfile { Id = 1, Name = "Lakshmi", Age = 68, Relation = "Mother" };
        var reading = new VitalsReading { Id = 5, ElderProfileId = 1, SystolicBp = 130 }; // everything else null
        var json = FhirExportService.BuildElderVitalsBundle(elder, [reading]);
        using var doc = JsonDocument.Parse(json);
        var observationCount = doc.RootElement.GetProperty("entry").EnumerateArray()
            .Count(e => e.GetProperty("resource").GetProperty("resourceType").GetString() == "Observation");
        Assert.Equal(1, observationCount); // only systolic, not four invented values
    }

    [Fact]
    public void ElderVitalsBundle_WithNoAbha_OmitsPatientIdentifier()
    {
        var elder = new ElderProfile { Id = 1, Name = "Lakshmi", Age = 68, Relation = "Mother", AbhaId = null };
        var json = FhirExportService.BuildElderVitalsBundle(elder, []);
        using var doc = JsonDocument.Parse(json);
        var patient = doc.RootElement.GetProperty("entry")[0].GetProperty("resource");
        Assert.False(patient.TryGetProperty("identifier", out _));
    }

    [Theory]
    [InlineData("12345678901234", "https://healthid.ndhm.gov.in")]
    [InlineData("ravi.kumar@abdm", "https://healthid.ndhm.gov.in/health-id")]
    public void ElderVitalsBundle_WithAbha_PicksTheSystemForTheIdForm(string abha, string expectedSystem)
    {
        var elder = new ElderProfile { Id = 1, Name = "Lakshmi", Age = 68, Relation = "Mother", AbhaId = abha };
        var json = FhirExportService.BuildElderVitalsBundle(elder, []);
        using var doc = JsonDocument.Parse(json);
        var identifier = doc.RootElement.GetProperty("entry")[0].GetProperty("resource").GetProperty("identifier")[0];
        Assert.Equal(expectedSystem, identifier.GetProperty("system").GetString());
        Assert.Equal(abha, identifier.GetProperty("value").GetString());
    }

    // ── Maternal vitals bundle ───────────────────────────────────────────

    [Fact]
    public void MaternalVitalsBundle_UsesTheSameSourcedLoincCodes()
    {
        var user = new UserProfile { Id = 1, UserId = 1, Name = "Anjali" };
        var reading = new PregnancyVitalsReading
        {
            Id = 7, UserId = 1, RecordedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            SystolicBp = 150, DiastolicBp = 95, HeartRate = 88, TemperatureC = 37.2m, OxygenSaturation = 97
        };
        var json = FhirExportService.BuildMaternalVitalsBundle(user, [reading]);
        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entry").EnumerateArray().ToList();

        var observations = entries
            .Select(e => e.GetProperty("resource"))
            .Where(r => r.GetProperty("resourceType").GetString() == "Observation")
            .ToList();
        Assert.Equal(5, observations.Count);

        string LoincOf(string display) => observations
            .Single(o => o.GetProperty("code").GetProperty("text").GetString() == display)
            .GetProperty("code").GetProperty("coding")[0].GetProperty("code").GetString()!;

        Assert.Equal("8480-6", LoincOf("Systolic blood pressure"));
        Assert.Equal("8462-4", LoincOf("Diastolic blood pressure"));
        Assert.Equal("8867-4", LoincOf("Heart rate"));
        Assert.Equal("8310-5", LoincOf("Body temperature"));
        Assert.Equal("59408-5", LoincOf("Oxygen saturation, pulse oximetry"));
    }

    [Fact]
    public void MaternalVitalsBundle_SkipsNullReadings_RatherThanFabricatingAValue()
    {
        var user = new UserProfile { Id = 1, UserId = 1, Name = "Anjali" };
        var reading = new PregnancyVitalsReading { Id = 7, UserId = 1, SystolicBp = 150 };
        var json = FhirExportService.BuildMaternalVitalsBundle(user, [reading]);
        using var doc = JsonDocument.Parse(json);
        var observationCount = doc.RootElement.GetProperty("entry").EnumerateArray()
            .Count(e => e.GetProperty("resource").GetProperty("resourceType").GetString() == "Observation");
        Assert.Equal(1, observationCount);
    }

    [Fact]
    public void MaternalVitalsBundle_WithAbha_IncludesPatientIdentifier()
    {
        var user = new UserProfile { Id = 1, UserId = 1, Name = "Anjali", AbhaId = "12345678901234" };
        var json = FhirExportService.BuildMaternalVitalsBundle(user, []);
        using var doc = JsonDocument.Parse(json);
        var identifier = doc.RootElement.GetProperty("entry")[0].GetProperty("resource").GetProperty("identifier")[0];
        Assert.Equal("https://healthid.ndhm.gov.in", identifier.GetProperty("system").GetString());
        Assert.Equal("12345678901234", identifier.GetProperty("value").GetString());
    }

    // ── Infant immunization bundle ───────────────────────────────────────

    [Fact]
    public void InfantImmunizationBundle_UsesCompletedStatus_AndOmitsPerformer()
    {
        var infant = new InfantProfile { Id = 2, Name = "Arjun", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow) };
        var record = new VaccinationRecord { Id = 9, InfantProfileId = 2, VaccineName = "BCG", GivenAt = DateTime.UtcNow };
        var json = FhirExportService.BuildInfantImmunizationBundle(infant, [record]);
        using var doc = JsonDocument.Parse(json);
        var imm = doc.RootElement.GetProperty("entry")[1].GetProperty("resource");

        Assert.Equal("Immunization", imm.GetProperty("resourceType").GetString());
        Assert.Equal("completed", imm.GetProperty("status").GetString());
        Assert.Equal("BCG", imm.GetProperty("vaccineCode").GetProperty("text").GetString());
        // See the file header: performer.actor is required by the ABDM profile but
        // Janani has no clinician-verified record of who administered it, so it's
        // omitted rather than invented.
        Assert.False(imm.TryGetProperty("performer", out _));
    }

    [Fact]
    public void InfantImmunizationBundle_OneEntryPerRecord_PlusThePatient()
    {
        var infant = new InfantProfile { Id = 2, Name = "Arjun", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow) };
        var records = new List<VaccinationRecord>
        {
            new() { Id = 1, InfantProfileId = 2, VaccineName = "BCG", GivenAt = DateTime.UtcNow },
            new() { Id = 2, InfantProfileId = 2, VaccineName = "OPV-1", GivenAt = DateTime.UtcNow },
        };
        var json = FhirExportService.BuildInfantImmunizationBundle(infant, records);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("entry").GetArrayLength()); // patient + 2 immunizations
    }
}
