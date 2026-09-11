# Janani 🌸

Most working women aren't caring for just one person. The same woman managing her own pregnancy is often also the one her aging parent calls when something feels off, and the one who'll be up at 3am with a newborn a few months from now. Janani is one app for all of that — built on Google Cloud, with fourteen specialized Gemini agents doing the talking and a plain, deterministic system doing the deciding.

Built for **Patchamomma 2026**, a Google Cloud program for women professionals building production-grade GCP apps.

## The part we're proudest of

A blood pressure reading or a baby's growth trend doesn't get judged by a language model. A fixed, testable checker decides whether it's crossed into dangerous territory — plain code, not a guess. The AI only ever steps in afterward, to explain what an already-flagged concern means, in a warm and human way. When something does cross the line, every caregiver sharing that profile is notified immediately, and the family's doctor gets an email automatically, so no one has to be the one who happened to notice in time.

We tried to hold the rest of the app to the same standard: reading recommendations and recipes come from real, curated datasets in BigQuery, not an LLM's best guess, and the medicine-lookup and elder-tutor agents are grounded in live Google Search or a fixed guide catalog rather than left to improvise.

## How it's put together

Two Cloud Run services that don't fully trust each other:

- **`janani-app`** — .NET 8 / Blazor Server. This is where all the data lives, where every safety check actually runs, and everything the user sees gets rendered. It's the web app, the installable PWA, and what the native Android app loads live (the mobile app doesn't bundle its own copy — it's a thin native shell pointed at this service).
- **`janani-agents`** — Python / FastAPI, built on Google's Agent Development Kit, talking to Gemini through Vertex AI. It never touches the database directly and never decides anything safety-related — it receives context that's already been assembled, and returns text or structured JSON. It's also locked down (`--no-allow-unauthenticated`), reachable only by `janani-app`'s own service account, not the open internet.

Everything else — Cloud SQL, BigQuery, Cloud Storage, Pub/Sub, Secret Manager, Firebase Cloud Messaging — is plumbing in service of those two.

## What's actually in here

- **Pregnancy**: a daily mood check-in that becomes an AI-generated plan, a companion to talk to, meal suggestions, a kick counter, water tracking, a birth plan builder, messages to a partner, a journal.
- **Postpartum**: a daily check-in (pain, bleeding, healing) run through the same deterministic-checker-then-AI-narrates pattern as vitals, a recovery plan, and the HBNC (Home Based Newborn Care — India's official ASHA-worker home-visit protocol) schedule, computed automatically from the real delivery date rather than tracked by hand.
- **Elder care**: vitals logging that feeds the deterministic alert system, trend charts, sharing across more than one caregiver, a PDF a caregiver can actually hand to a doctor, and **Elder Mode** — a large-text, voice-friendly view built to be handed to the elder themselves, with an AI tutor that only ever answers from a real, curated guide (how to video call, send a photo, use the SOS button) rather than guessing at instructions for an app it's never seen.
- **Infant care**: growth, feeding, and sleep tracking, the India UIP vaccination schedule, the same alerting and sharing model as elder care.
- **Everywhere else**: an SOS/emergency flow, medicine reminders with a lookup grounded in live Google Search, recipes you can browse or just ask for like you'd ask the companion, **Family Care** — one AI agent that answers a single question spanning whichever roles apply to her at once (pregnancy, elder, infant), including government entitlement lookups — government scheme info, appointments, a public landing page for anyone visiting before they log in, and a seeded demo account so a reviewer isn't looking at an empty shell.
- **Language**: 15 supported. English, Hindi, and Telugu are fully localized — the app's own UI text, not just what the AI says back to you. The rest cover AI-generated responses for now.

