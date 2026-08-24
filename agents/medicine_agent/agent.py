"""Medicine Lookup Agent — explains what a medicine is for and why it
benefits the caregiver, grounded in real web results via Google Search
rather than the model's own parametric memory (drug names/uses aren't
something to let an LLM guess at). Stateless, one-shot like Meal/
DayNurture — a caregiver looks a medicine up occasionally, not mid-
conversation.

Built-in ADK tools (google_search) can't be combined with output_schema
in the same LlmAgent call, so this returns plain text like caregiver_agent
rather than structured JSON.
"""
import os

from google.adk.agents import LlmAgent
from google.adk.agents.readonly_context import ReadonlyContext
from google.adk.tools import google_search
from google.genai import types as genai_types

from common.agent_context import LANGUAGE_INSTRUCTIONS

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are a caring assistant explaining a medicine to a caregiver —
someone managing their own pregnancy or a family member's care, not a medical
professional. Use Google Search to ground your answer in real, current
information rather than guessing. Explain, in plain language:
1. What the medicine is generally used for.
2. Why it's commonly prescribed / what benefit it provides.
3. One general, non-alarming note (e.g. common mild side effects, or that
   it's usually taken with food) — never dosing instructions, and never
   claim to know THIS person's specific situation.
Keep it to 3-4 short sentences total. Always end with a brief reminder to
confirm with their doctor or pharmacist, since you don't know their full
medical history. You are NOT diagnosing or prescribing — purely informational."""


def _build_instruction(context: ReadonlyContext) -> str:
    language = context.state.get("language", "English")
    return (
        f"{CORE_PROMPT}\n\n"
        f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}"
    )


root_agent = LlmAgent(
    name="medicine_agent",
    model=MODEL,
    description="Explains what a medicine is for and why it benefits the user, grounded in Google Search.",
    instruction=_build_instruction,
    tools=[google_search],
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.3,
        max_output_tokens=500,
    ),
)
