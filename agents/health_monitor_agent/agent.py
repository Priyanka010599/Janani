"""Health Monitor Agent â€” explains an already-flagged vitals concern.

New agent, no prior C# equivalent. The trigger decision (is this reading
concerning at all) is deliberately NOT made here â€” see
Services/VitalsThresholdChecker.cs, which is plain code and always runs
first. This agent is only called once a concern already exists, to turn
raw threshold violations into a warm, clear explanation for the caregiver.
Keeping the actual alert decision out of the LLM's hands is the point:
routing/hallucination risk is a real concern raised in ADK's own
multi-agent docs, and that's not acceptable for something safety-critical.
"""
import os
from typing import List

from google.adk.agents import LlmAgent
from google.genai import types as genai_types
from pydantic import BaseModel

from common.agent_context import EMERGENCY_GUIDANCE
from common.elder_context import build_elder_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """A vitals reading for this elder has already been flagged as a concern by
a deterministic check â€” you are NOT deciding whether this is concerning, that has
already been decided. Your job is only to explain the flagged concern(s) clearly
and calmly to the caregiver, and suggest a reasonable next step (e.g. "monitor
and note it", "call her doctor today", "seek urgent medical care now" for
anything severe). You are NOT a doctor â€” do not diagnose a cause. Be direct
about the concern without being alarmist; caregivers need clarity, not panic."""


class HealthAlertExplanation(BaseModel):
    explanation: str
    suggestedAction: str


root_agent = LlmAgent(
    name="health_monitor_agent",
    model=MODEL,
    description="Explains an already-flagged vitals concern in clear, caregiver-friendly language.",
    instruction=build_elder_instruction(CORE_PROMPT + EMERGENCY_GUIDANCE),
    output_schema=HealthAlertExplanation,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.4,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1000,
    ),
)
