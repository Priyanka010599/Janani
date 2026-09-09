# Janani / Bloom Companion — Design & Architecture Docs

| Doc | What it covers |
|---|---|
| [`offline-first-design.md`](offline-first-design.md) | Design narrative: why the deterministic checker + Gemini narration pipeline was built source-agnostic on seed data before real edge hardware exists. |
| [`architecture-vitals-pipeline.md`](architecture-vitals-pipeline.md) | Zoomed-in diagram: dedup → deterministic threshold checker → Gemini narration (with fallback) → Pub/Sub/FCM alert dispatch. |
| [`architecture-whole-system.md`](architecture-whole-system.md) | Zoomed-out diagram: both Cloud Run services, all domain modules, the 13 Gemini agents, data layer, auth/family-sharing model, and GCP integrations. |

Each Markdown doc renders its diagram as Mermaid (GitHub-native). Styled HTML versions of all three — with the original visual design — are in [`html/`](html/).

Status legend used throughout: 🟢 live today · 🟡 planned hardware, not built · 🔵 designed for, not yet implemented.
