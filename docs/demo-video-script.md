# Janani — Demo Video Script & Capture Plan

**For:** Patchamomma 2026 hackathon submission
**Format:** Screen-recording walkthrough with voiceover
**Target length:** ~6:30 (hard cap 8:00 — trim Act 5 first if over)
**Audience:** Judges seeing the app for the first time — assume no prior context, show real workflows, name the architecture once.

---

## 1. Production setup

### What to record against

| Option | URL | Use when |
|---|---|---|
| **Deployed (preferred)** | `https://janani-app-52541450553.us-central1.run.app` | Default. AI agents, Vertex, Pub/Sub, doctor email all live. Nothing to start locally. |
| Local | `http://localhost:5033` (`dotnet run` in `D:\LLM\Janani`) | Only if the deployment is down. AI features need the agents service reachable — set `AGENT_SERVICE_URL` to the deployed `janani-agents` and `AGENT_SERVICE_REQUIRES_AUTH=false`, or expect AI panels to show their fallback text. |

The demo account is seeded automatically on every startup (idempotent). No manual data entry.

### Demo credentials

- **Username:** `demo`
- **Password:** `JananiDemo2026!`

Seeded persona — **Priya**, a working woman who is simultaneously:
- **28 weeks pregnant** (mood check-in, journal, kick session, appointment for an anomaly scan, prenatal vitamins)
- caring for her mother **Lakshmi, 68**, managed hypertension — 14 days of BP history, an acknowledged warning, and **one active un-acknowledged critical alert (188/96)**
- caring for her infant **Arjun, 22 weeks** — 22 weekly growth points, feeds, sleep, first three vaccines recorded

That single overlap *is* the story. Keep coming back to it.

### Tools (Windows)

- **Recorder:** OBS Studio (free) — Display or Window Capture, 1920×1080, 30 fps, MP4. Xbox Game Bar (`Win+G`) works for a single unbroken take.
- **Mic:** headset or external mic, not the laptop mic. Record voiceover live, or record silent and dub after (dubbing gives a cleaner result and lets you match pacing).
- **Editor:** Clipchamp (ships with Windows), DaVinci Resolve (free), or CapCut — for captions, zoom-in callouts, trims.
- **Cursor:** enable click highlighting in OBS or the editor so judges can follow taps.

### Pre-flight checklist

- [ ] Log in once before recording so the session is warm; then log out for the real take.
- [ ] Browser at 100% zoom, full-screen (`F11`), bookmarks bar hidden, no extensions visible.
- [ ] Close notifications (`Win+A` → Focus assist / Do not disturb on).
- [ ] Hide desktop clutter; neutral wallpaper.
- [ ] Do a 20-second throwaway recording and check audio levels + text legibility at final resolution.
- [ ] Have the seeded facts above on a second screen so you don't fumble names/numbers.
- [ ] Pre-open the AI-heavy pages once so responses are cached-warm and don't stall on camera. If a live AI call is slow during the take, keep narrating — you'll trim the wait in the edit.

### Recording approach

Record in **five separate takes**, one per Act. Easier to redo a fumble, and you cut the AI wait times between Acts. Keep the mouse still while talking; move deliberately when you move.

---

## 2. The script

Legend — **[DO]** = on-screen action, **[SAY]** = voiceover, **[EDIT]** = post note.

---

### COLD OPEN — 0:00–0:20

**[DO]** Start on the public landing page (open the deployed URL in a fresh tab, logged out). Slow scroll through the hero and feature sections.

**[SAY]**
> "Most working women aren't caring for just one person. The same woman managing her own pregnancy is often the one her ageing parent calls when something feels off — and the one who'll be up at 3 a.m. with a newborn a few months from now. Janani is one app for all of that."

**[EDIT]** Title card over the last two seconds: **Janani — one app for the whole household.** Soft background music from here, ducked under voice for the rest.

---

### ACT 1 — The one account that holds all three — 0:20–1:15

**[DO]** Click **Log in**. Type `demo` / `JananiDemo2026!`. Land on Home.

**[SAY]**
> "This is Priya's account. She's 28 weeks pregnant. Up here in the navigation" — **[DO]** hover the nav groups: Pregnancy, Newborn & Postpartum, Elderly Care — "the app has reshaped itself around what she actually does: her own pregnancy, her mother's elder care, and her baby. She set those three at signup; nothing here is a demo mock-up — it's one person's real load."

**[DO]** On Home, point out the **daily mood check-in** tile and today's AI-generated plan.

**[SAY]**
> "Her day starts with a thirty-second mood check-in. That becomes an AI-generated plan for the day — but notice what's driving it: her mood, plus her pregnancy week. The language model writes the plan; it doesn't decide anything medical. Hold that thought."

**[EDIT]** Callout arrow on the three nav groups as they're named.

---

### ACT 2 — The part we're proudest of: deterministic safety + AI narration — 1:15–3:15

*This is the centrepiece. Slow down. Don't rush the alert.*

**[DO]** Open **Elderly Care → Elder Care**. Show Lakshmi's profile: age 68, "Managed hypertension."

**[SAY]**
> "This is Lakshmi, Priya's mother. Sixty-eight, hypertension, on daily medication. Priya logs her blood pressure — or a connected device does."

**[DO]** Scroll to the **vitals trend chart**. Show the 14-day line, then the spike at the latest reading.

**[SAY]**
> "Two weeks of readings, sitting in a normal band — until this morning. One-eighty-eight over ninety-six."

**[DO]** Scroll to the **active critical alert** (the un-acknowledged one). Read the message on screen.

**[SAY]**
> "Here's the important bit. A language model did **not** look at that number and decide it was dangerous. A fixed, testable checker — plain code — did. It crossed a hard threshold, so it's flagged Critical. The AI only steps in *after* that, to explain what an already-flagged reading means, in plain, warm language a worried daughter can act on."

**[DO]** Point to the AI explanation text under the alert.

**[SAY]**
> "And when something crosses the line, Priya isn't the only one who finds out. Every caregiver who shares Lakshmi's profile is notified straight away, and the family's doctor gets an email automatically — so nobody has to be the one who happened to check in time."

**[DO]** Show the acknowledged earlier **Warning** alert just below, to make the severity ladder visible.

**[SAY]**
> "A milder reading last week was just a nudge — rest and recheck. Same system, different threshold. The judgement is deterministic; only the wording is AI."

**[DO]** Click **Export** (Elder → PDF for doctor). Show the generated PDF preview / download.

**[SAY]**
> "And this is what a caregiver actually hands to a doctor — the history as a clean PDF, not a screenshot."

**[EDIT]** When you say "plain code, not a guess," freeze-frame 1 s on the alert with a caption: **Threshold check = deterministic. AI = explanation only.** This line is the whole pitch.

---

### ACT 3 — Elder Mode: the view you hand to the elder — 3:15–4:00

**[DO]** From Elder Care, open **Elder Mode**. Show the large-text, high-contrast layout.

**[SAY]**
> "Elder Care has a second face. Elder Mode is a large-text, voice-friendly view built to be handed to Lakshmi herself — video call, send a photo, the SOS button, all oversized."

**[DO]** Use the **AI tutor** in Elder Mode — ask something like *"How do I make a video call?"* Show the answer.

**[SAY]**
> "The tutor here only ever answers from a real, curated guide — how to video call, how to send a photo, how to use SOS. It will not improvise instructions for an app it's never seen. If it's not in the guide, it says so."

**[EDIT]** Split-screen for 3 s: normal Elder Care on the left, Elder Mode on the right, to show the same data, two audiences.

---

### ACT 4 — Infant care, same spine — 4:00–4:45

**[DO]** Open **Newborn & Postpartum → Infant Care**. Show Arjun's **growth chart** (22 weekly points), the **feeding** and **sleep** logs, and the **UIP vaccination schedule** with BCG / OPV-0 / Hep-B done.

**[SAY]**
> "Arjun is twenty-two weeks. Growth, feeding, sleep — and India's Universal Immunization Programme schedule, with what's done and what's coming. The reassuring part: it's the exact same alerting and family-sharing model as elder care. A growth trend that falls off its curve gets flagged the same deterministic way, and reaches every caregiver the same way. One spine, three life stages."

**[EDIT]** Quick lower-third: **Same deterministic checker + caregiver fan-out as Act 2.**

---

### ACT 5 — Everything a caregiving day also needs — 4:45–5:45

*Montage pace. ~10–12 s per stop. Cut the AI waits.*

**[DO / SAY]** in sequence:

1. **Companion** (Pregnancy) — type *"Is it safe to travel by train at 28 weeks?"*
   > "A pregnancy companion, grounded in its own curated knowledge base — not the open web."
2. **Medicines** — open the lookup for a drug name.
   > "Medicine reminders, with a lookup grounded in live Google Search, not the model's memory."
3. **Recipes** — browse the catalog, then ask for one in natural language.
   > "Recipes come from a real curated dataset in BigQuery — you can browse them or just ask, like you'd ask the companion."
4. **Kicks / Water / Journal** — quick pass: log a kick, a glass of water, open the seeded journal entry "Felt the first flutter."
   > "The small daily things — kick counter, water, a journal — are right here too."
5. **Appointments** — show the seeded "Anomaly scan" with questions to ask.
   > "Her next scan, with the questions she wants to remember to ask."
6. **SOS** — open the Emergency page (do **not** trigger it). Show the emergency contacts (Dr. Anita Shah, Karthik).
   > "And one SOS flow, reachable from every screen."

**[EDIT]** This whole act can run under continuous music with your voice lighter. If you're over length, cut stops 4 and 5 first.

---

### ACT 6 — What's under it, and language — 5:45–6:20

**[DO]** Open **Settings**, switch language **English → Hindi → Telugu**. Show the nav bar and a page re-rendering in each.

**[SAY]**
> "Fifteen languages. English, Hindi and Telugu are fully localized — the app's own text, not just the AI's replies."

**[DO]** Switch back to English. Optionally show `docs/html/architecture-whole-system.html` or the mermaid diagram as a full-screen still.

**[SAY]**
> "Underneath: two Cloud Run services that don't fully trust each other. A .NET Blazor app that holds all the data and runs every safety check, and a private Python service — Google's Agent Development Kit, thirteen Gemini agents on Vertex AI — that only ever narrates or drafts. It's not reachable from a browser at all. The AI makes it warm. The plain, testable code keeps it safe."

---

### CLOSE — 6:20–6:30

**[DO]** Return to the Home screen, or the landing page hero.

**[SAY]**
> "Janani. One app for the woman holding the whole household together. Built on Google Cloud for Patchamomma 2026."

**[EDIT]** End card: **Janani 🌸** · deployed URL · "Patchamomma 2026". Music resolves.

---

## 3. On-screen text / caption cues (for the editor)

| Time | Caption |
|---|---|
| 0:18 | Janani — one app for the whole household |
| 0:40 | Pregnancy · Elder care · Infant care — one account |
| 2:05 | Deterministic threshold check → AI explanation only |
| 2:40 | Critical alert → all caregivers notified + doctor emailed |
| 3:20 | Elder Mode — same data, built for the elder |
| 4:10 | Infant care — same checker, same fan-out |
| 6:00 | 2 Cloud Run services · 13 Gemini agents · Vertex AI · ADK |
| 6:25 | Built on Google Cloud · Patchamomma 2026 |

## 4. Pickup shots (record after, drop in during edit)

- Slow pan of the vitals trend chart (for Act 2 opening).
- The critical alert card, static, 3 s (for the freeze-frame).
- Nav groups expanding, one by one (for Act 1 callout).
- The doctor PDF scrolling.
- Language switch, tight on the nav bar only.

## 5. Things to get right / avoid

- **Do** let the critical alert breathe — 8–10 seconds on it. It's the differentiator.
- **Do** say "deterministic" / "plain code" / "fixed checker" at least twice. Judges should leave knowing the LLM doesn't make safety calls.
- **Don't** trigger the real SOS flow or send a real doctor email on camera — describe them.
- **Don't** show the login password field un-blurred if you slow-mo it; or just use the seeded creds since they're already public in this doc.
- **Don't** let a slow AI response dead-air the video — keep talking, trim in edit.
- If Hindi/Telugu rendering looks half-translated on any page, switch back to English before that page is on screen — only nav + core pages are fully localized.
