# Designing for Offline-First Health Data Before You Have the Hardware

### Deterministic safety checks meet seed-data-driven AI

*Architecture & Design · Janani / Bloom Companion · ~9 min read*

> Styled version: [`html/offline-first-design.html`](html/offline-first-design.html)

---

Most health-tech AI demos I come across start the same way: a sensor is already streaming, and the interesting part of the write-up is the model. I built Bloom Companion, the caregiving module inside my family health app Janani, in the opposite order. I designed and shipped the deterministic safety-check pipeline and the LLM narration layer *first*, running entirely on realistic seed data — specifically so the architecture would already be connectivity-agnostic and hardware-agnostic by the time a real edge sensor is ready to plug in. This is a post about that design discipline, not a "here's my live sensor" post. If you're expecting a hardware teardown, you won't find one here — what you'll find is the argument for why the wiring underneath should be built before the wire exists.

**Legend:** 🟢 Live today · 🟡 Planned hardware · 🔵 Designed for, not built

## What Does It Mean to Design "Offline-First" Before You're Offline?

"Offline-first" usually gets talked about as a runtime property — a device that keeps working without a network, then syncs when it reconnects. But there's a design-time version of that idea that matters just as much, and it's the one I want to talk about here.

The pattern is this: build your data contract and your decision layer assuming a reading could arrive late, out of order, or in a batch — *even while every reading right now is actually seeded and simulated.* You're not claiming your hardware handles offline sync. You're claiming your code doesn't secretly assume the opposite.

That distinction matters because the failure mode is subtle. It's easy to write a vitals pipeline that quietly assumes "exactly one reading arrives, right now, from a trusted live source." That assumption is invisible while you're feeding it seed data in insertion order. It only surfaces once a real device starts store-and-forwarding three hours of missed readings after a Wi-Fi outage — at which point it's not a bug fix, it's a rewrite of your severity logic, your data model, and possibly your alerting.

So the discipline is: treat "arrival order and timing are not guaranteed" as a constraint on the architecture today, regardless of whether anything is actually arriving out of order today. It's cheap to design for. It's expensive to retrofit.

## Use Case Overview — Bloom Companion (Janani)

Janani is a family caregiving app built to unify two things that are usually handled by separate apps and separate mental models: maternal and infant health monitoring, and elder-care remote monitoring. In practice, one caregiver is often managing both ends of a household at once — a new mother recovering postpartum with an infant to track, and an aging parent whose vitals need watching from a distance. Bloom Companion is the module that watches vitals for both populations and turns thresholds crossed into something a caregiver can act on, explained in plain language rather than a raw number.

To be direct about where things stand: **today, every vitals reading in Bloom Companion comes from seed data**, exercised through the exact same code paths a real sensor feed would use. There is no live sensor wired into the app. An edge device — an ESP32 with a MAX30102 pulse-oximetry sensor, with on-device inference via Qualcomm AI Hub — is a hardware phase planned next. It does not exist in this codebase yet, in any form, not even as a stub endpoint. It's named here as roadmap, not as something already scaffolded in the repo, because the "why design this way now" argument only makes sense if you know what's coming.

## Problem

Given that framing, the architecture has to satisfy three constraints *before* real hardware exists — not as future-proofing for its own sake, but because getting these wrong now means re-architecting later instead of just swapping a data source.

**1. The severity-check logic must be pure and deterministic.** Thresholds go in, a verdict comes out — the same verdict every time, regardless of whether the input came from seed data, a batch upload from an offline device, or a live stream. No code path in the checker should be written as if "always online, always exactly one reading at a time" is a safe assumption, because it's already false in a batch-upload world and it'll be false the day a device buffers readings during a dead zone.

**2. The LLM explanation layer must never be the thing making the call.** It narrates a verdict the deterministic checker already produced. That has to hold true regardless of where the reading came from — seed data today, a device tomorrow. If the model's job ever quietly expands from "explain this decision" to "help decide this," you've built a system whose safety behavior depends on prompt behavior, and that's not a trade worth making in a health context.

**3. The data model has to be designed now for out-of-order and batched arrival**, even though nothing arrives out of order today. Timestamps, sync markers, dedup keys — these need to exist in the schema from day one so that swapping seed data for a real store-and-forward edge device later is a *data-source change*, not a *re-architecture*.

## How We Do It Today 🟢

The actual stack, as it exists right now:

| Service | Stack | Role |
|---|---|---|
| `janani-app` | .NET 8, Blazor Server, EF Core, Cloud SQL Postgres 15 | Owns the deterministic severity-checker logic; runs it against seeded vitals and events |
| `janani-agents` | Python, FastAPI, Google ADK, Gemini 2.5 Flash on Vertex AI | Narrates a verdict the .NET side already computed, in plain language a caregiver can read. Does not compute severity |

The seed data isn't arbitrary — it's deliberately shaped like plausible sensor output. Readings carry timestamps and a `Source` field (currently a free-text string — `null` for a manually entered reading, or a device-name string like `"Withings BPM Connect (simulated)"` for a simulated feed) so the checker code is already being exercised the way it will need to behave once a real edge device is integrated, rather than being written against an idealized, order-guaranteed stream.

## Architecture

Three layers, each marked by what's actually running versus what's designed-for:

**(a) Data source layer** — 🟢 Today: a seed data generator (`SeedDemoAccount` in `Program.cs`) populates `VitalsReading` and `PregnancyVitalsReading` records. 🟡 Planned: an ESP32 + MAX30102 edge sensor, with on-device inference via Qualcomm AI Hub — not yet built, named here as intent.

**(b) Deterministic checker layer** 🟢 — `Services/VitalsThresholdChecker.cs` is a pure static class. It takes a `VitalsReading` and returns a list of `Concern` objects based on threshold checks — systolic/diastolic blood pressure, heart rate, temperature, SpO2. No network call, no LLM call. Sibling checkers (`PregnancyVitalsThresholdChecker`, `GrowthThresholdChecker`, `PostpartumRecoveryChecker`, `EpdsScreeningChecker`) follow the same shape.

**(c) Cloud layer** 🟢 — Cloud SQL Postgres 15 for storage; a `janani-alert-events` Pub/Sub topic that fans out to FCM push notifications, with a direct-FCM fallback path so a Pub/Sub outage never means a critical alert is silently dropped; Gemini narration via Vertex AI, called only after the checker layer has already produced a verdict.

## Step-by-Step Implementation Guide

### Step 1 — The seed data shape 🟢

```csharp
public class VitalsReading
{
    public int Id { get; set; }
    public int ElderProfileId { get; set; }
    public DateTime RecordedAt { get; set; }
    public int? SystolicBp { get; set; }
    public int? DiastolicBp { get; set; }
    public int? HeartRate { get; set; }
    public decimal? TemperatureC { get; set; }
    public decimal? OxygenSaturation { get; set; }
    public string? Notes { get; set; }
    public string? Source { get; set; }   // null = manual entry,
                                           // or a device name string,
                                           // e.g. "Withings BPM Connect (simulated)"
}
```

`Source` today is a loose, human-readable string rather than a clean enum — a gap worth tightening (e.g. a `SourceType` of `Manual` / `SimulatedDevice` / `EdgeDevice`) before a real device is wired in, so ingestion code can branch on it reliably rather than string-matching.

### Step 2 — The deterministic severity-check function 🟢

```csharp
public static class VitalsThresholdChecker
{
    public static List<Concern> Check(VitalsReading v)
    {
        var concerns = new List<Concern>();

        if (v.SystolicBp is < 90 or > 180)
            concerns.Add(new Concern(Severity.Critical, "Systolic BP out of safe range"));

        if (v.DiastolicBp is < 60 or > 120)
            concerns.Add(new Concern(Severity.Critical, "Diastolic BP out of safe range"));

        if (v.HeartRate is < 50 or > 120)
            concerns.Add(new Concern(Severity.Warning, "Heart rate out of typical range"));

        if (v.TemperatureC is < 35.0m or > 38.5m)
            concerns.Add(new Concern(Severity.Warning, "Temperature out of typical range"));

        if (v.OxygenSaturation is < 92)
            concerns.Add(new Concern(Severity.Critical, "SpO2 below safe threshold"));

        return concerns;
        // Note: thresholds are a starting point for triage, not medical guidance.
    }
}
```

This is the piece that makes "source-agnostic by design" real rather than aspirational: a pure function over a record. Feed it a seeded row, a batch-uploaded row, or a live-streamed row, and it behaves identically, because it has no opinion about arrival.

### Step 3 — Handling out-of-order/batched records 🟢 dedup / 🔵 ordering

Dedup is real, backed at both the application and database layer. `Services/VitalsIngestService.cs` checks a client-generated `DeviceReadingId` against existing rows before insert, and a partial unique index enforces the same rule at the database:

```csharp
// Services/VitalsIngestService.cs
if (!string.IsNullOrEmpty(reading.DeviceReadingId) &&
    await db.VitalsReadings.AsNoTracking()
        .AnyAsync(v => v.DeviceReadingId == reading.DeviceReadingId, ct))
{
    return new VitalsIngestResult(Duplicate: true, AlertRaised: false, Severity: null, Alert: null);
}
```

```sql
-- Program.cs — partial index, so it never conflicts with
-- the many manually entered readings that have no DeviceReadingId
CREATE UNIQUE INDEX IF NOT EXISTS "IX_VitalsReadings_DeviceReadingId"
ON "VitalsReadings" ("DeviceReadingId")
WHERE "DeviceReadingId" IS NOT NULL;
```

**Honest gap that remains:** this closes the "retried duplicate upload" case, but not full out-of-order handling. The ingest endpoint still assumes a device flushes its buffer in capture order once it reconnects — there's no server-side timestamp-reordering yet.

The part of the original design bet that did pay off: none of this touched the checker from Step 2. Dedup happens entirely before `VitalsThresholdChecker.Check()` is ever called — the checker still takes one record at a time and has no idea a duplicate check happened upstream.

### Step 4 — Pub/Sub → FCM alert dispatch 🟢

```csharp
public async Task NotifyUserViaPubSubAsync(string userId, Alert alert)
{
    var published = await _pubSub.PublishAsync("janani-alert-events", alert);

    if (!published)
    {
        // Direct-FCM fallback: an infra hiccup upstream of Pub/Sub
        // must not mean a critical alert never reaches the caregiver.
        await NotifyUserAsync(userId, alert);
    }
}

private async Task NotifyUserAsync(string userId, Alert alert)
{
    var url = $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send";
    // ... builds and sends the FCM v1 payload directly
}
```

This layer genuinely doesn't care where the reading that triggered the alert came from — it only knows a verdict was produced.

### Step 5 — Gemini narration 🟢

```python
class ExplainRequest(BaseModel):
    concerns: list[str]
    severity: str

@app.post("/agents/health-monitor/explain")
async def explain(req: ExplainRequest):
    # Narrates a verdict already produced by VitalsThresholdChecker.
    # Does not re-derive or override the severity.
    return await health_monitor_agent.run(req.concerns, req.severity)
```

**Honest gap:** narration receives a single verdict's worth of pre-formatted concerns, not a list of recent readings. Giving the model a short window of history so it can narrate a trend rather than a single crossed threshold is on the list, but isn't built.

### Step 6 — What changes when the edge device is integrated 🟢 simulator / 🟡 real hardware

There's now a real `edge-device-sim/vitals_edge_simulator.py` in the repo — a standalone script whose docstring says exactly what it is: it "simulates the planned ESP32 + MAX30102 (Qualcomm AI Hub) edge vitals device talking to Bloom Companion / Janani, before any real hardware is wired in." It's still software, not firmware — but it's no longer just a name in a roadmap. It POSTs real readings to the real `/api/elders/{id}/vitals` and `/api/pregnancy/vitals` endpoints, runs the same dedup/check/alert pipeline as the seed data and the manual-entry UI, and simulates the offline store-and-forward pattern (buffer while offline, flush in capture order on reconnect, survive a duplicate retry) entirely client-side.

When the real device is ready, the intended change is narrower than it once was: swap the simulator's client-side reading generation for actual MAX30102 sensor reads and Qualcomm AI Hub on-device inference, keeping the same request shape and the same `DeviceReadingId` the simulator already sends. Everything downstream — the checker in Step 2, the Pub/Sub/FCM dispatch in Step 4, the Gemini narration in Step 5 — stays untouched, because none of it was ever written to assume where a reading came from.

## Closing Reflection

None of this is impressive to look at running — it's seed data producing the same alerts a demo could fake in an afternoon. The value isn't in what it looks like today; it's in what it doesn't need to change tomorrow. Designing the deterministic/narration split, and shaping the data model for out-of-order and batched arrival, before there's any real hardware generating messy data, is what should make the eventual sensor integration a data-source swap instead of a rewrite of the safety logic underneath it.

> 💡 **Key Takeaway:** Decisions about determinism, ordering, and dedup are cheap to get right before you have real hardware generating messy, late, or batched data — and expensive to retrofit once a device is already in the field depending on the old assumptions. If you're building health-adjacent software and the sensor isn't ready yet, that's not a reason to wait on the architecture. It's the best possible time to get it right.

---

*This work is part of Google Cloud's Patchamomma program, which supports women professionals building production-grade GCP applications in Data, AI, and Databases. The edge hardware phase — ESP32, MAX30102, and on-device inference via Qualcomm AI Hub — is next on the roadmap.*
