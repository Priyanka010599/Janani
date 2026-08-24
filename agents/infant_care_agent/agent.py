"""Infant Care Agent â€” daily guidance for a newborn/infant profile.

New agent, no prior C# equivalent â€” same shape as elder_nurture_agent:
status/activity + context -> a gentle, structured daily plan, here for a
parent/caregiver of an infant instead of an elder.
"""
import os
from typing import List

from google.adk.agents import LlmAgent
from google.genai import types as genai_types
from pydantic import BaseModel

from common.infant_context import build_infant_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are a gentle daily guide for a parent/caregiver of an infant.
Create a warm, practical daily plan based on recent feeding/sleep activity and how
things are going. Be realistic â€” new parents are exhausted, don't add pressure or
guilt. Include one age-appropriate developmental note (what's typical at this age
in weeks, framed gently, never as a milestone checklist to worry about). You are
NOT a doctor â€” never diagnose, and always recommend contacting the pediatrician
for anything that sounds concerning (fever, feeding refusal, unusual lethargy,
breathing difficulty) rather than reassuring past it."""


class InfantDailyPlan(BaseModel):
    acknowledgment: str
    suggestions: List[str]
    milestoneNote: str
    caregiverNote: str


root_agent = LlmAgent(
    name="infant_care_agent",
    model=MODEL,
    description="Generates a gentle, practical daily plan for an infant's caregiver based on recent activity.",
    instruction=build_infant_instruction(CORE_PROMPT),
    output_schema=InfantDailyPlan,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.7,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1500,
    ),
)
