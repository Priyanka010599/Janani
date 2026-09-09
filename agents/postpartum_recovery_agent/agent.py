"""Postpartum Recovery Agent -- explains an already-flagged postpartum
recovery concern.

Postpartum-side counterpart to pregnancy_vitals_agent. The trigger decision
is deliberately NOT made here -- see Services/PostpartumRecoveryChecker.cs
and Services/EpdsScreeningChecker.cs, both plain code that always run
first. This agent is only called once a concern already exists, covering
BOTH the daily physical check-in (pain, bleeding, temperature, wound) and
the EPDS-10 mental health screening -- the same "explain, don't decide"
shape applies to both, so one agent serves both call sites.
"""
import os

from google.adk.agents import LlmAgent
from google.genai import types as genai_types
from pydantic import BaseModel

from common.agent_context import EMERGENCY_GUIDANCE, build_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """A postpartum recovery concern has already been flagged by a deterministic
check -- you are NOT deciding whether this is concerning, that has already been decided.
Your job is only to explain the flagged concern(s) clearly and calmly, and suggest a
reasonable next step (e.g. "mention it at your next visit", "call your doctor today",
"seek urgent care now" for anything severe).

If the flagged concern involves thoughts of self-harm (from an EPDS mental health
screening), treat this with particular care: acknowledge her feelings first, be direct
that this needs support right now, and name both her provider AND a crisis resource --
in India, the Tele-MANAS helpline (14416 or 1-800-891-4416, available 24/7 in English
and 20 regional languages) -- rather than only a routine "mention it at your next visit".

Be direct about the concern without being alarmist; she needs clarity, not panic."""


class PostpartumAlertExplanation(BaseModel):
    explanation: str
    suggestedAction: str


root_agent = LlmAgent(
    name="postpartum_recovery_agent",
    model=MODEL,
    description="Explains an already-flagged postpartum recovery or EPDS mental-health concern in clear, reassuring language.",
    instruction=build_instruction(CORE_PROMPT + EMERGENCY_GUIDANCE),
    output_schema=PostpartumAlertExplanation,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.4,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1000,
    ),
)
