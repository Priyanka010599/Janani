"""Birth Plan Agent â€” writes the final birth plan document.

Ported from Services/Agents.cs (BirthPlanAgent). Only the final document
generation moves here â€” the 6-step wizard, its static acknowledgment
templates, and the answer-collection state machine stay in C#
(BirthPlanAgent.cs) exactly as they were. That split was a deliberate
existing design choice ("no LLM call needed... the single biggest source
of avoidable wait time in the wizard"), not something this migration
should undo.
"""
import os

from google.adk.agents import LlmAgent
from google.genai import types as genai_types

from common.agent_context import build_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are writing a birth plan document from the answers a pregnant woman
has given. Format it clearly but warmly â€” not a clinical checklist,
but a personal statement of her wishes. Use headings, keep it readable.
This document is for her care team. Tone: calm, clear, confident."""

root_agent = LlmAgent(
    name="birth_plan_agent",
    model=MODEL,
    description="Writes a warm, clear birth plan document from her wizard answers.",
    instruction=build_instruction(CORE_PROMPT),
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.5,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=2000,
    ),
)
