"""Standalone demo: simulates the planned ESP32 + MAX30102 (Qualcomm AI Hub)
edge vitals device talking to Bloom Companion / Janani, before any real
hardware is wired in.

This talks to the real Janani app — it POSTs each reading to a real ingest
endpoint, which runs the real IngestService (dedup -> save -> check ->
explain -> alert -> notify), the same pipeline the Blazor UI's manual entry
and simulated device sync use. There is no local "safety check"
reimplementation here and nothing is written to disk — the server's response
for each upload is printed instead.

Two profile types, matching the two vitals pipelines that actually exist:

  elder:     POST /api/elders/{elderId}/vitals — a shared DEVICE_INGEST_TOKEN
             works because elderId only ever points at a dependent profile.
             pip install requests
             python vitals_edge_simulator.py --profile-type elder --elder-id 1 --base-url http://localhost:5000

  pregnancy: POST /api/pregnancy/vitals — UserId IS the real account, so
             there's no shared secret; instead each user generates their own
             pairing code in Settings (UserProfile.DevicePairingToken) and
             that's what proves the upload belongs to them.
             python vitals_edge_simulator.py --profile-type pregnancy --user-id 1 --pairing-token <code from Settings> --base-url http://localhost:5000

The offline-first store-and-forward pattern (buffer while offline, flush in
capture order on reconnect, survive a flaky-network duplicate retry) is still
simulated entirely client-side, exactly as a real ESP32 with local flash
storage would behave, regardless of profile type — only the network call at
the end is real.

The reading-generation ranges are kept in sync by hand with the real
threshold checkers (VitalsThresholdChecker for elder,
PregnancyVitalsThresholdChecker for pregnancy — pregnancy has two BP tiers,
elder has one) — there's no shared code between them on purpose, since this
script is meant to run standalone. But the "is this dangerous" decision is
never made here: the real checker on the server decides, this script just
reports what it said.
"""
import argparse
import random
import sys
import uuid
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Literal

import requests

# Windows consoles often default stdout to cp1252, which can't encode the
# emoji this script prints -- force UTF-8 so it doesn't crash mid-run.
if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8")

ProfileType = Literal["elder", "pregnancy"]


@dataclass
class VitalsReading:
    reading_id: str
    subject_name: str
    captured_at: str
    systolic_bp: int
    diastolic_bp: int
    heart_rate: int
    temperature_c: float
    oxygen_saturation: int
    source: str


def generate_reading(rng: random.Random, profile_type: ProfileType, subject_name: str, captured_at: datetime) -> VitalsReading:
    if profile_type == "pregnancy":
        # Mirrors PregnancyVitalsThresholdChecker's two BP tiers (>=140/90
        # warning, >=160/110 critical) and fever tiers (>=38 warning, >=39
        # critical) — different shape from elder's single-tier checks.
        roll = rng.random()
        if roll < 0.10:
            systolic, diastolic = rng.randint(162, 195), rng.randint(112, 128)
        elif roll < 0.25:
            systolic, diastolic = rng.randint(142, 158), rng.randint(92, 108)
        else:
            systolic, diastolic = rng.randint(105, 132), rng.randint(65, 85)
        temp_roll = rng.random()
        temperature_c = round(39.2 + rng.uniform(0, 0.6), 1) if temp_roll < 0.06 \
            else round(38.2 + rng.uniform(0, 0.7), 1) if temp_roll < 0.15 \
            else round(36.6 + rng.uniform(-0.2, 0.5), 1)
        spo2 = rng.randint(87, 91) if rng.random() < 0.08 else rng.randint(95, 99)
    else:
        abnormal = rng.random() < 0.25
        systolic = rng.randint(182, 205) if abnormal else rng.randint(105, 135)
        diastolic = rng.randint(65, 90)
        temperature_c = round(36.5 + rng.uniform(-0.2, 0.6), 1)
        spo2 = rng.randint(86, 91) if (abnormal and rng.random() < 0.4) else rng.randint(94, 99)

    return VitalsReading(
        reading_id=str(uuid.uuid4()),
        subject_name=subject_name,
        captured_at=captured_at.isoformat(),
        systolic_bp=systolic,
        diastolic_bp=diastolic,
        heart_rate=rng.randint(62, 95),
        temperature_c=temperature_c,
        oxygen_saturation=spo2,
        source="ESP32-MAX30102 (simulated)",
    )


class EdgeDevice:
    """Owns the local buffer a real ESP32 would keep in flash while offline,
    plus the sync step that flushes it once connectivity returns. The
    client-side synced_ids check is the device's own retry-safety net (a real
    device shouldn't even attempt re-uploading something it already got an ack
    for); the server independently enforces the same idempotency via
    DeviceReadingId, which is what actually protects the data if the device's
    own bookkeeping is ever lost (e.g. a reboot mid-retry)."""

    def __init__(self, base_url: str, profile_type: ProfileType, target_id: int, secret: str | None):
        self.buffer: list[VitalsReading] = []
        self.base_url = base_url.rstrip("/")
        self.profile_type = profile_type
        self.target_id = target_id
        self.secret = secret
        self.synced_ids: set[str] = set()

    def capture(self, reading: VitalsReading) -> None:
        self.buffer.append(reading)

    def _upload(self, reading: VitalsReading) -> dict:
        payload = {
            "SystolicBp": reading.systolic_bp,
            "DiastolicBp": reading.diastolic_bp,
            "HeartRate": reading.heart_rate,
            "TemperatureC": reading.temperature_c,
            "OxygenSaturation": reading.oxygen_saturation,
            "Source": reading.source,
            "CapturedAt": reading.captured_at,
            "DeviceReadingId": reading.reading_id,
        }
        if self.profile_type == "elder":
            url = f"{self.base_url}/api/elders/{self.target_id}/vitals"
            params = {"token": self.secret} if self.secret else None
        else:
            url = f"{self.base_url}/api/pregnancy/vitals"
            params = None
            payload["UserId"] = self.target_id
            payload["PairingToken"] = self.secret

        response = requests.post(url, json=payload, params=params, timeout=10)
        response.raise_for_status()
        return response.json()

    def sync(self) -> tuple[int, int]:
        """Flush the buffer in capture order. Returns (uploaded, alerts_raised)."""
        uploaded = 0
        alerts_raised = 0
        for reading in self.buffer:
            if reading.reading_id in self.synced_ids:
                print(f"    ⚠️  duplicate upload skipped client-side (reading_id={reading.reading_id[:8]}…) — already synced")
                continue

            try:
                result = self._upload(reading)
            except requests.RequestException as exc:
                print(f"    ❌ upload failed for reading_id={reading.reading_id[:8]}…: {exc}")
                continue

            self.synced_ids.add(reading.reading_id)
            uploaded += 1

            if result.get("duplicate"):
                print(f"    ⚠️  server rejected duplicate (reading_id={reading.reading_id[:8]}…) — DeviceReadingId already recorded")
            elif result.get("alertRaised"):
                severity = result.get("severity")
                icon = "🚨" if severity == "Critical" else "⚠️ "
                print(f"    {icon} [{severity}] alert raised by the server (real check + explanation, not simulated)")
                alerts_raised += 1

        self.buffer.clear()
        return uploaded, alerts_raised

    def resend(self, reading: VitalsReading) -> None:
        """Simulate a flaky-network retry resending something already synced."""
        self.buffer.append(reading)


def run_demo(base_url: str, profile_type: ProfileType, target_id: int, secret: str | None,
             ticks: int, offline_start: int, offline_length: int, seed: int, subject_name: str) -> None:
    rng = random.Random(seed)
    device = EdgeDevice(base_url, profile_type, target_id, secret)

    start = datetime.now(timezone.utc)
    interval = timedelta(minutes=15)
    offline_end = offline_start + offline_length
    max_buffered = 0
    last_synced_reading: VitalsReading | None = None
    total_uploaded = 0
    total_alerts = 0

    endpoint = f"/api/elders/{target_id}/vitals" if profile_type == "elder" else "/api/pregnancy/vitals"
    print(f"Simulating {subject_name}'s vitals cuff ({profile_type}) — {ticks} readings, 15 min apart.")
    print(f"Connectivity drops from reading #{offline_start} to #{offline_end - 1} (rural signal gap).")
    print(f"Uploading to {base_url}{endpoint}\n")

    for tick in range(ticks):
        online = not (offline_start <= tick < offline_end)
        captured_at = start + tick * interval
        reading = generate_reading(rng, profile_type, subject_name, captured_at)
        device.capture(reading)
        max_buffered = max(max_buffered, len(device.buffer))

        status = "captured" if online else "captured (offline — buffered on-device)"
        print(f"[{captured_at.strftime('%H:%M')}] #{tick:02d} {status}: "
              f"BP {reading.systolic_bp}/{reading.diastolic_bp}, HR {reading.heart_rate}, "
              f"SpO2 {reading.oxygen_saturation}%, Temp {reading.temperature_c}°C")

        if online:
            just_reconnected = tick == offline_end and offline_length > 0
            if just_reconnected:
                print(f"    🔌 network restored — syncing {len(device.buffer)} buffered readings in capture order")
            synced, alerts = device.sync()
            total_uploaded += synced
            total_alerts += alerts
            if synced:
                last_synced_reading = reading

    print("\n🔁 simulating a flaky retry: resending the last synced reading...")
    if last_synced_reading:
        device.resend(last_synced_reading)
        synced, alerts = device.sync()
        total_uploaded += synced
        total_alerts += alerts

    print("\n--- summary ---")
    print(f"readings captured:      {ticks}")
    print(f"longest offline buffer: {max_buffered} readings held on-device at once")
    print(f"readings uploaded:      {total_uploaded}  (should equal {ticks}, not {ticks + 1}, despite the retry)")
    print(f"alerts raised (server): {total_alerts}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-url", type=str, default="http://localhost:5000", help="Janani app base URL")
    parser.add_argument("--profile-type", choices=["elder", "pregnancy"], default="elder", help="which vitals pipeline to demo")

    parser.add_argument("--elder-id", type=int, default=None, help="[elder] ElderProfileId to upload readings for")
    parser.add_argument("--token", type=str, default=None, help="[elder] DEVICE_INGEST_TOKEN, if the server has one configured")
    parser.add_argument("--elder-name", type=str, default="Lakshmi", help="[elder] matches the seeded demo elder's name")

    parser.add_argument("--user-id", type=int, default=None, help="[pregnancy] UserId to upload readings for")
    parser.add_argument("--pairing-token", type=str, default=None, help="[pregnancy] UserProfile.DevicePairingToken, generated in Settings")
    parser.add_argument("--user-name", type=str, default="Mama", help="[pregnancy] display name for narration only")

    parser.add_argument("--ticks", type=int, default=20, help="number of readings to simulate")
    parser.add_argument("--offline-start", type=int, default=5, help="reading index where connectivity drops")
    parser.add_argument("--offline-length", type=int, default=8, help="how many readings are captured while offline")
    parser.add_argument("--seed", type=int, default=42, help="RNG seed, for reproducible demo output")
    args = parser.parse_args()

    if args.profile_type == "elder":
        if args.elder_id is None:
            parser.error("--profile-type elder requires --elder-id")
        run_demo(args.base_url, "elder", args.elder_id, args.token,
                  args.ticks, args.offline_start, args.offline_length, args.seed, args.elder_name)
    else:
        if args.user_id is None or not args.pairing_token:
            parser.error("--profile-type pregnancy requires --user-id and --pairing-token")
        run_demo(args.base_url, "pregnancy", args.user_id, args.pairing_token,
                  args.ticks, args.offline_start, args.offline_length, args.seed, args.user_name)
