"""Caregiver Coordination Agent â€” answers a caregiver's questions across
everyone they care for, and can inform them about JSY/JSSK entitlements.

New agent, no prior C# equivalent. Stateless (one-shot per question) like
Meal/DayNurture rather than persisted like Companion â€” this is a periodic
"how's everyone doing" / "what am I entitled to" lookup, not an ongoing
conversation thread. The "scheme navigator" is content injected into the
instruction context (same RAG-by-injection approach companion_agent uses
for its pregnancy knowledge base), not a separate callable tool â€” kept
consistent with the one pattern already proven in this codebase rather
than introducing ADK's formal tool-calling machinery for a first pass.
"""
import os

from google.adk.agents import LlmAgent
from google.adk.agents.readonly_context import ReadonlyContext
from google.genai import types as genai_types

from caregiver_agent.scheme_navigator import get_scheme_content
from common.agent_context import LANGUAGE_INSTRUCTIONS

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are a helpful assistant for a family caregiver â€” of any gender â€”
helping them keep track of the people they care for and understand what support â€”
including government entitlements â€” may be available. You are NOT a doctor: never
give medical advice, only informational and logistical help. Ground entitlement
questions in the SCHEME INFORMATION below, and always remind the caregiver to
confirm current details with their local health facility or ASHA worker,
since amounts and rules vary by state and change over time. Keep answers
warm but concise â€” caregiving is exhausting, don't make them read a wall of text."""


def _build_instruction(context: ReadonlyContext) -> str:
    state = context.state
    language = state.get("language", "English")
    lines = [
        CORE_PROMPT,
        "",
        f"CAREGIVER: {state.get('caregiver_name', 'there')}",
        "",
        "WHO THEY CARE FOR:",
        state.get("care_summary", "No one added yet."),
        "",
        "SCHEME INFORMATION:",
        get_scheme_content(),
        "",
        f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}",
    ]
    return "\n".join(lines)


root_agent = LlmAgent(
    name="caregiver_agent",
    model=MODEL,
    description="Answers a caregiver's questions across everyone they care for, including government entitlement info.",
    instruction=_build_instruction,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.6,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1200,
    ),
)
