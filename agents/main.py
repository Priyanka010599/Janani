"""Janani Agent Service — FastAPI wrapper around ADK agents.

Reference-pattern service: each Phase 3 agent gets its own module under
agents/ and its own route here, following the same shape: build per-request
session state, run one turn via common.runner_helpers.run_single_turn,
parse the structured result.
"""
import uuid
from typing import List, Optional

from dotenv import load_dotenv

load_dotenv()  # local dev only — Cloud Run sets real env vars/secrets directly

from fastapi import FastAPI, HTTPException
from fastapi.responses import StreamingResponse
from google.adk.runners import Runner
from google.adk.sessions import InMemorySessionService
from pydantic import BaseModel

from birth_plan_agent.agent import root_agent as birth_plan_agent
from caregiver_agent.agent import root_agent as caregiver_agent
from common.runner_helpers import run_persisted_turn, run_single_turn, stream_turn
from common.session_store import build_session_service
from companion_agent.agent import root_agent as companion_agent
from day_nurture_agent.agent import DailyPlan
from day_nurture_agent.agent import root_agent as day_nurture_agent
from elder_nurture_agent.agent import ElderDailyPlan
from elder_nurture_agent.agent import root_agent as elder_nurture_agent
from elder_tutor_agent.agent import root_agent as elder_tutor_agent
from health_monitor_agent.agent import HealthAlertExplanation
from health_monitor_agent.agent import root_agent as health_monitor_agent
from infant_care_agent.agent import InfantDailyPlan
from infant_care_agent.agent import root_agent as infant_care_agent
from meal_agent.agent import MealSuggestion
from meal_agent.agent import root_agent as meal_agent
from medicine_agent.agent import root_agent as medicine_agent
from partner_agent.agent import root_agent as partner_agent
from recipe_agent.agent import root_agent as recipe_agent

APP_NAME = "janani-agents"

app = FastAPI(title="Janani Agent Service")

# Stateless agents (Meal, DayNurture): throwaway in-memory session per request.
session_service = InMemorySessionService()
meal_runner = Runner(agent=meal_agent, app_name=APP_NAME, session_service=session_service)
day_nurture_runner = Runner(agent=day_nurture_agent, app_name=APP_NAME, session_service=session_service)
partner_runner = Runner(agent=partner_agent, app_name=APP_NAME, session_service=session_service)
birth_plan_runner = Runner(agent=birth_plan_agent, app_name=APP_NAME, session_service=session_service)
health_monitor_runner = Runner(agent=health_monitor_agent, app_name=APP_NAME, session_service=session_service)
caregiver_runner = Runner(agent=caregiver_agent, app_name=APP_NAME, session_service=session_service)
medicine_runner = Runner(agent=medicine_agent, app_name=APP_NAME, session_service=session_service)
recipe_runner = Runner(agent=recipe_agent, app_name=APP_NAME, session_service=session_service)
elder_tutor_runner = Runner(agent=elder_tutor_agent, app_name=APP_NAME, session_service=session_service)

# Companion, Elder Care and Infant Care: real persisted memory — see
# common/session_store.py. Elder/infant profile ids are already globally
# unique (not per-user), so they double as both user_id and session_id below
# without needing the caregiver's own userId threaded through.
companion_session_service = build_session_service()
companion_runner = Runner(agent=companion_agent, app_name=APP_NAME, session_service=companion_session_service)
elder_nurture_runner = Runner(agent=elder_nurture_agent, app_name=APP_NAME, session_service=companion_session_service)
infant_care_runner = Runner(agent=infant_care_agent, app_name=APP_NAME, session_service=companion_session_service)


class SuggestMealsRequest(BaseModel):
    feeling: str
    userName: str = "Mama"
    pregnancyWeek: int = 20
    language: str = "English"
    workingWomanMode: bool = True
    lastMood: Optional[str] = None


class DailyPlanRequest(BaseModel):
    moodDescription: str
    timeOfDay: str
    additionalNote: Optional[str] = None
    userName: str = "Mama"
    pregnancyWeek: int = 20
    language: str = "English"
    workingWomanMode: bool = True
    lastMood: Optional[str] = None


class CompanionChatRequest(BaseModel):
    userId: int
    question: str
    userName: str = "Mama"
    pregnancyWeek: int = 20
    language: str = "English"
    workingWomanMode: bool = True
    lastMood: Optional[str] = None


class PartnerMessageRequest(BaseModel):
    additionalContext: Optional[str] = None
    userName: str = "Mama"
    pregnancyWeek: int = 20
    language: str = "English"
    workingWomanMode: bool = True
    lastMood: Optional[str] = None


class BirthPlanRequest(BaseModel):
    answersText: str
    userName: str = "Mama"
    pregnancyWeek: int = 20
    language: str = "English"
    workingWomanMode: bool = True
    lastMood: Optional[str] = None


class ElderDailyPlanRequest(BaseModel):
    elderId: int
    moodDescription: str
    elderName: str
    elderAge: int
    elderRelation: str
    recentVitalsSummary: Optional[str] = None
    additionalNote: Optional[str] = None
    language: str = "English"


class HealthAlertRequest(BaseModel):
    elderName: str
    elderAge: int
    elderRelation: str
    concerns: List[str]
    severity: str
    language: str = "English"


class CaregiverQuestionRequest(BaseModel):
    question: str
    caregiverName: str = "there"
    careSummary: str = "No one added yet."
    language: str = "English"


class MedicineLookupRequest(BaseModel):
    medicineName: str
    dosage: Optional[str] = None
    language: str = "English"


class RecipeAskRequest(BaseModel):
    question: str
    recipeCatalog: str
    language: str = "English"


class ElderTutorAskRequest(BaseModel):
    question: str
    guideCatalog: str
    language: str = "English"


class InfantDailyPlanRequest(BaseModel):
    infantId: int
    statusDescription: str
    infantName: str
    infantAgeWeeks: int
    recentActivitySummary: Optional[str] = None
    language: str = "English"


@app.get("/health")
async def health():
    agents = [
        meal_agent, day_nurture_agent, companion_agent, partner_agent,
        birth_plan_agent, elder_nurture_agent, health_monitor_agent,
        caregiver_agent, infant_care_agent, medicine_agent, recipe_agent, elder_tutor_agent,
    ]
    return {"status": "Healthy", "agents": [a.name for a in agents], "model": meal_agent.model}


@app.post("/agents/meal/suggest", response_model=MealSuggestion)
async def suggest_meals(req: SuggestMealsRequest):
    final_text = await run_single_turn(
        meal_runner,
        session_service,
        APP_NAME,
        state={
            "user_name": req.userName,
            "pregnancy_week": req.pregnancyWeek,
            "language": req.language,
            "working_woman_mode": req.workingWomanMode,
            "last_mood": req.lastMood,
        },
        message_text=(
            f"She is feeling {req.feeling}. Pregnancy week {req.pregnancyWeek}. "
            "Suggest gentle, nourishing meals."
        ),
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return MealSuggestion.model_validate_json(final_text)


@app.post("/agents/day-nurture/plan", response_model=DailyPlan)
async def generate_daily_plan(req: DailyPlanRequest):
    note_line = f'She also mentioned: "{req.additionalNote}"' if req.additionalNote else ""
    final_text = await run_single_turn(
        day_nurture_runner,
        session_service,
        APP_NAME,
        state={
            "user_name": req.userName,
            "pregnancy_week": req.pregnancyWeek,
            "language": req.language,
            "working_woman_mode": req.workingWomanMode,
            "last_mood": req.lastMood,
        },
        message_text=(
            f"She is feeling {req.moodDescription} this {req.timeOfDay}. "
            f"{note_line} Create a gentle, realistic daily plan."
        ),
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return DailyPlan.model_validate_json(final_text)


@app.post("/agents/partner/message")
async def generate_partner_message(req: PartnerMessageRequest):
    mood_line = f"She has been feeling {req.lastMood} lately." if req.lastMood else ""
    context_line = f"She wants to mention: {req.additionalContext}" if req.additionalContext else ""
    final_text = await run_single_turn(
        partner_runner,
        session_service,
        APP_NAME,
        state={
            "user_name": req.userName,
            "pregnancy_week": req.pregnancyWeek,
            "language": req.language,
            "working_woman_mode": req.workingWomanMode,
            "last_mood": req.lastMood,
        },
        message_text=(
            f"Write a message from {req.userName} to her partner.\n"
            f"She is {req.pregnancyWeek} weeks pregnant.\n"
            f"{mood_line}\n{context_line}"
        ),
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return {"message": final_text.strip()}


@app.post("/agents/elder-nurture/plan", response_model=ElderDailyPlan)
async def generate_elder_daily_plan(req: ElderDailyPlanRequest):
    note_line = f'The caregiver also mentioned: "{req.additionalNote}"' if req.additionalNote else ""
    session_id = f"elder-{req.elderId}"
    final_text = await run_persisted_turn(
        elder_nurture_runner,
        companion_session_service,
        APP_NAME,
        user_id=session_id,
        session_id=session_id,
        state_delta={
            "elder_name": req.elderName,
            "elder_age": req.elderAge,
            "elder_relation": req.elderRelation,
            "recent_vitals_summary": req.recentVitalsSummary,
            "language": req.language,
        },
        message_text=(
            f"{req.elderName} seems to be {req.moodDescription} today. "
            f"{note_line} Create a gentle, realistic daily caregiving plan."
        ),
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return ElderDailyPlan.model_validate_json(final_text)


@app.post("/agents/health-monitor/explain", response_model=HealthAlertExplanation)
async def explain_health_alert(req: HealthAlertRequest):
    concerns_text = "; ".join(req.concerns)
    final_text = await run_single_turn(
        health_monitor_runner,
        session_service,
        APP_NAME,
        state={
            "elder_name": req.elderName,
            "elder_age": req.elderAge,
            "elder_relation": req.elderRelation,
            "language": req.language,
        },
        message_text=(
            f"Severity: {req.severity}. Flagged concern(s): {concerns_text}. "
            "Explain this to the caregiver and suggest a next step."
        ),
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return HealthAlertExplanation.model_validate_json(final_text)


@app.post("/agents/infant-care/plan", response_model=InfantDailyPlan)
async def generate_infant_daily_plan(req: InfantDailyPlanRequest):
    session_id = f"infant-{req.infantId}"
    final_text = await run_persisted_turn(
        infant_care_runner,
        companion_session_service,
        APP_NAME,
        user_id=session_id,
        session_id=session_id,
        state_delta={
            "infant_name": req.infantName,
            "infant_age_weeks": req.infantAgeWeeks,
            "recent_activity_summary": req.recentActivitySummary,
            "language": req.language,
        },
        message_text=(
            f"{req.infantName} — {req.statusDescription}. "
            "Create a gentle, realistic daily plan for the caregiver."
        ),
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return InfantDailyPlan.model_validate_json(final_text)


@app.post("/agents/caregiver/ask")
async def ask_caregiver_agent(req: CaregiverQuestionRequest):
    final_text = await run_single_turn(
        caregiver_runner,
        session_service,
        APP_NAME,
        state={
            "caregiver_name": req.caregiverName,
            "care_summary": req.careSummary,
            "language": req.language,
        },
        message_text=req.question,
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return {"answer": final_text.strip()}


@app.post("/agents/medicine/lookup")
async def lookup_medicine(req: MedicineLookupRequest):
    dosage_line = f" (dosage on record: {req.dosage})" if req.dosage else ""
    final_text = await run_single_turn(
        medicine_runner,
        session_service,
        APP_NAME,
        state={"language": req.language},
        message_text=f"What is {req.medicineName}{dosage_line} used for, and why is it beneficial?",
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return {"explanation": final_text.strip()}


@app.post("/agents/recipe/ask")
async def ask_recipe_agent(req: RecipeAskRequest):
    final_text = await run_single_turn(
        recipe_runner,
        session_service,
        APP_NAME,
        state={"recipe_catalog": req.recipeCatalog, "language": req.language},
        message_text=req.question,
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return {"answer": final_text.strip()}


@app.post("/agents/elder-tutor/ask")
async def ask_elder_tutor_agent(req: ElderTutorAskRequest):
    final_text = await run_single_turn(
        elder_tutor_runner,
        session_service,
        APP_NAME,
        state={"guide_catalog": req.guideCatalog, "language": req.language},
        message_text=req.question,
    )
    if final_text is None:
        raise HTTPException(status_code=502, detail="Agent produced no response")
    return {"answer": final_text.strip()}


@app.post("/agents/birth-plan/generate")
async def generate_birth_plan(req: BirthPlanRequest):
    # Stateless like Meal/DayNurture (fresh session per request) — the wizard
    # only ever calls this once, at the end, so there's nothing to persist.
    session_id = str(uuid.uuid4())

    async def token_stream():
        async for chunk in stream_turn(
            birth_plan_runner,
            session_service,
            APP_NAME,
            user_id="birth-plan-request",
            session_id=session_id,
            state_delta={
                "user_name": req.userName,
                "pregnancy_week": req.pregnancyWeek,
                "language": req.language,
                "working_woman_mode": req.workingWomanMode,
                "last_mood": req.lastMood,
            },
            message_text=(
                f"Create a birth plan for {req.userName} (week {req.pregnancyWeek}) "
                f"from these answers:\n\n{req.answersText}"
            ),
        ):
            yield chunk

    return StreamingResponse(token_stream(), media_type="text/plain")


@app.post("/agents/companion/chat")
async def companion_chat(req: CompanionChatRequest):
    session_id = f"companion-{req.userId}"

    async def token_stream():
        async for chunk in stream_turn(
            companion_runner,
            companion_session_service,
            APP_NAME,
            user_id=str(req.userId),
            session_id=session_id,
            state_delta={
                "user_name": req.userName,
                "pregnancy_week": req.pregnancyWeek,
                "language": req.language,
                "working_woman_mode": req.workingWomanMode,
                "last_mood": req.lastMood,
            },
            message_text=req.question,
        ):
            yield chunk

    return StreamingResponse(token_stream(), media_type="text/plain")


@app.delete("/agents/companion/session")
async def clear_companion_session(userId: int):
    await companion_session_service.delete_session(
        app_name=APP_NAME, user_id=str(userId), session_id=f"companion-{userId}"
    )
    return {"status": "cleared"}
