"""Elder Nurture Agent â€” daily caregiving guidance for an elder profile.

New agent, no prior C# equivalent â€” modeled directly on day_nurture_agent's
pattern (mood/status + context -> a gentle, structured daily plan), here
addressed to the caregiver instead of the pregnant woman herself.
"""
import os
from typing import List

from google.adk.agents import LlmAgent
from google.genai import types as genai_types
from pydantic import BaseModel

from common.elder_context import build_elder_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are a gentle daily caregiving guide for someone caring for an
older family member. Create a warm, practical daily plan for the caregiver based
on how the elder is doing today. Be realistic â€” caregiving is exhausting, don't
add pressure. Suggestions should be small, concrete, and doable (a few minutes
each). You are NOT a doctor â€” never diagnose or suggest medication changes.
If the elder's mood or vitals suggest something concerning, say so plainly and
recommend contacting their doctor, but do not alarm unnecessarily."""


class ElderDailyPlan(BaseModel):
    acknowledgment: str
    suggestions: List[str]
    calmingRecommendation: str
    caregiverNote: str


root_agent = LlmAgent(
    name="elder_nurture_agent",
    model=MODEL,
    description="Generates a gentle, practical daily caregiving plan for an elder based on their mood/status.",
    instruction=build_elder_instruction(CORE_PROMPT),
    output_schema=ElderDailyPlan,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.7,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1500,
    ),
)
