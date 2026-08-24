"""Day Nurture Agent â€” mood-based daily plan.

Ported from Janani/Services/DayNurtureService.cs. Mood-to-description
mapping and the per-circuit result cache stay in the .NET side (pure
lookup/UI-perf concerns, not agent behavior) â€” this service just takes
the already-resolved mood description and time of day.
"""
import os
from typing import List

from google.adk.agents import LlmAgent
from google.genai import types as genai_types
from pydantic import BaseModel

from common.agent_context import build_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are Janani's daily nurturer. Create a gentle, personalized daily
plan that meets her where she is emotionally. Be warm, non-judgmental, and
realistic. Don't push her to be productive if she's tired. Keep suggestions
simple and achievable (max 5 minutes each). The week note should be one
encouraging fact or gentle reminder about her current week of pregnancy.
If she is exhausted or struggling, validate that fully before any suggestion."""


class DailyPlan(BaseModel):
    acknowledgment: str
    suggestions: List[str]
    calmingRecommendation: str
    affirmation: str
    weekNote: str


root_agent = LlmAgent(
    name="day_nurture_agent",
    model=MODEL,
    description="Generates a gentle, personalized daily plan from her mood and pregnancy week.",
    instruction=build_instruction(CORE_PROMPT),
    output_schema=DailyPlan,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.7,
        # See meal_agent for why this is nonzero but small â€” leaves room in
        # max_output_tokens for the actual JSON after hidden thinking tokens.
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1500,
    ),
)
