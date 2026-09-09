"""Pregnancy Vitals Agent — explains an already-flagged pregnancy vitals concern.

Pregnancy-side counterpart to health_monitor_agent. The trigger decision (is
this reading concerning at all) is deliberately NOT made here — see
Services/PregnancyVitalsThresholdChecker.cs, which is plain code and always
runs first. This agent is only called once a concern already exists, to turn
raw threshold violations into a warm, clear explanation.

Uses the standard common/agent_context.py instruction builder (not a
dedicated one like elder's) since pregnancy vitals share the same
name/week/language state shape every other pregnancy-side agent already
populates — no divergent context fields like elder's age/relation.
"""
import os

from google.adk.agents import LlmAgent
from google.genai import types as genai_types
from pydantic import BaseModel

from common.agent_context import build_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """A vitals reading during her pregnancy has already been flagged as a concern by
a deterministic check — you are NOT deciding whether this is concerning, that has
already been decided. Your job is only to explain the flagged concern(s) clearly
and calmly, and suggest a reasonable next step (e.g. "mention it at your next
visit", "call your OB-GYN today", "go to labor & delivery or the ER now" for
anything severe). Some flagged concerns, like high blood pressure, can be signs
of preeclampsia — you may gently name that possibility without alarming her, but
never diagnose, and always defer next steps to her doctor. Be direct about the
concern without being alarmist; she needs clarity, not panic."""


class HealthAlertExplanation(BaseModel):
    explanation: str
    suggestedAction: str


root_agent = LlmAgent(
    name="pregnancy_vitals_agent",
    model=MODEL,
    description="Explains an already-flagged pregnancy vitals concern in clear, reassuring language.",
    instruction=build_instruction(CORE_PROMPT),
    output_schema=HealthAlertExplanation,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.4,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1000,
    ),
)
