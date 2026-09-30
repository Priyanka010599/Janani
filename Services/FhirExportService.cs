// Services/FhirExportService.cs
// Builds an HL7 FHIR R4 JSON export of an elder's vitals or an infant's immunisation
// history, so a PHC medical officer's EMR can ingest Janani data instead of retyping it.
//
// SOURCED, not invented -- checked against the National Resource Centre for EHR
// Standards' FHIR Implementation Guide for ABDM (nrces.in/ndhm/fhir/r4, 2026-09):
//   * ObservationVitalSigns profile: status, code (LOINC-coded), subject, effective[x]
//     and value[x] are the elements this export actually populates.
//   * LOINC codes for the five vitals below are the standard, universal codes used by
//     HL7's own base vital-signs profile (not ABDM-specific): 8480-6 (systolic BP),
//     8462-4 (diastolic BP), 8867-4 (heart rate), 8310-5 (body temperature),
//     59408-5 (oxygen saturation, pulse oximetry).
//   * Immunization profile: status, vaccineCode, patient and occurrenceDateTime are
//     the elements this export populates.
//
// WHAT THIS DELIBERATELY DOES NOT CLAIM:
//   * This is a best-effort FHIR R4 Bundle for portability, not a validated, ABDM-
//     conformant payload. It has not been run through an official FHIR/ABDM validator.
//   * Bundle.type is "collection", not the "document" type the India Patient Summary
//     profile actually requires (that needs a Composition resource with narrative
//     sections, which this does not build).
//   * Immunization.performer.actor is a required (1..1) element in the ABDM profile --
//     Janani has no record of who administered a vaccine (self-reported by a
//     caregiver, not a clinician), so it's omitted rather than fabricated.
//   * Immunization.vaccineCode uses each vaccine's own name as `display` with a
//     Janani-internal `system`, not a code from ABDM's ndhm-vaccine-codes ValueSet --
//     that value set's actual codes weren't available to confirm during this pass.
// A PHC officer should treat this as a structured starting point to review, not a
// payload to push into ABDM's Health Information Exchange as-is.

using System.Text.Json;
using System.Text.Json.Serialization;
using Janani.Models;

namespace Janani.Services;

public static class FhirExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // A 14-digit ABHA number, or an ABHA address (name@abdm / name@sbx -- the sandbox
    // suffix) -- per ABDM's own description of the two forms an ABHA identity takes.
    public static bool IsValidAbhaId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return true; // optional field
        id = id.Trim();
        if (id.Contains('@')) return id.Length > 2 && id.IndexOf('@') > 0;
        return id.Length == 14 && id.All(char.IsDigit);
    }

    public static string BuildElderVitalsBundle(ElderProfile elder, IReadOnlyList<VitalsReading> readings)
    {
        var patientId = "patient-elder-" + elder.Id;
        var entries = new List<object> { PatientEntry(patientId, elder.Name, elder.AbhaId) };

        foreach (var r in readings)
        {
            if (r.SystolicBp is { } sys)
                entries.Add(VitalEntry(patientId, r, "8480-6", "Systolic blood pressure", sys, "mm[Hg]"));
            if (r.DiastolicBp is { } dia)
                entries.Add(VitalEntry(patientId, r, "8462-4", "Diastolic blood pressure", dia, "mm[Hg]"));
            if (r.HeartRate is { } hr)
                entries.Add(VitalEntry(patientId, r, "8867-4", "Heart rate", hr, "/min"));
            if (r.TemperatureC is { } temp)
                entries.Add(VitalEntry(patientId, r, "8310-5", "Body temperature", temp, "Cel"));
            if (r.OxygenSaturation is { } spo2)
                entries.Add(VitalEntry(patientId, r, "59408-5", "Oxygen saturation, pulse oximetry", spo2, "%"));
        }

        return Bundle(entries);
    }

    public static string BuildInfantImmunizationBundle(InfantProfile infant, IReadOnlyList<VaccinationRecord> given)
    {
        var patientId = "patient-infant-" + infant.Id;
        var entries = new List<object> { PatientEntry(patientId, infant.Name, infant.AbhaId) };

        foreach (var v in given)
        {
            entries.Add(new
            {
                fullUrl = "urn:uuid:imm-" + v.Id,
                resource = new
                {
                    resourceType = "Immunization",
                    id = "imm-" + v.Id,
                    meta = new { profile = new[] { "https://nrces.in/ndhm/fhir/r4/StructureDefinition/Immunization" } },
                    status = "completed",
                    vaccineCode = new
                    {
                        coding = new[] { new { system = "https://janani.app/fhir/vaccine-codes", code = Slug(v.VaccineName), display = v.VaccineName } },
                        text = v.VaccineName
                    },
                    patient = new { reference = "urn:uuid:" + patientId },
                    occurrenceDateTime = v.GivenAt.ToString("yyyy-MM-ddTHH:mm:sszzz")
                }
            });
        }

        return Bundle(entries);
    }

    private static string Slug(string name) => name.Trim().ToLowerInvariant().Replace(' ', '-');

    private static object PatientEntry(string id, string name, string? abhaId)
    {
        var identifiers = new List<object>();
        if (!string.IsNullOrWhiteSpace(abhaId))
        {
            identifiers.Add(new
            {
                system = abhaId.Contains('@') ? "https://healthid.ndhm.gov.in/health-id" : "https://healthid.ndhm.gov.in",
                value = abhaId
            });
        }

        return new
        {
            fullUrl = "urn:uuid:" + id,
            resource = new
            {
                resourceType = "Patient",
                id,
                identifier = identifiers.Count > 0 ? identifiers : null,
                name = new[] { new { text = name } }
            }
        };
    }

    private static object VitalEntry<T>(string patientId, VitalsReading r, string loincCode, string display, T value, string unit)
    {
        return new
        {
            fullUrl = $"urn:uuid:obs-{r.Id}-{loincCode}",
            resource = new
            {
                resourceType = "Observation",
                id = $"obs-{r.Id}-{loincCode}",
                meta = new { profile = new[] { "https://nrces.in/ndhm/fhir/r4/StructureDefinition/ObservationVitalSigns" } },
                status = "final",
                category = new[]
                {
                    new { coding = new[] { new { system = "http://terminology.hl7.org/CodeSystem/observation-category", code = "vital-signs", display = "Vital Signs" } } }
                },
                code = new
                {
                    coding = new[] { new { system = "http://loinc.org", code = loincCode, display } },
                    text = display
                },
                subject = new { reference = "urn:uuid:" + patientId },
                effectiveDateTime = r.RecordedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                valueQuantity = new { value, unit, system = "http://unitsofmeasure.org", code = unit }
            }
        };
    }

    private static string Bundle(List<object> entries)
    {
        var bundle = new
        {
            resourceType = "Bundle",
            type = "collection", // see file header: not "document" -- no Composition/IN-PS narrative built here
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            entry = entries
        };
        return JsonSerializer.Serialize(bundle, JsonOptions);
    }
}
