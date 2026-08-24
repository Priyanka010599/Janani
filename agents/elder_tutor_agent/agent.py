"""Elder Tutor Agent — answers an elder's free-text question about using
their phone or the Janani app (e.g. "how do I video call my daughter",
"what is this app") by selecting from a curated, real guide catalog —
never inventing steps of its own. Giving an elderly, possibly-confused
user WRONG step-by-step instructions for an app is actively harmful, not
just inaccurate, so this agent is held to the same discipline as
medicine_agent/recipe_agent: the catalog (injected into context) is the
single source of truth, and the agent's only job is to pick from it and
explain simply — never guess at UI steps it wasn't given.
"""
import os

from google.adk.agents import LlmAgent
from google.adk.agents.readonly_context import ReadonlyContext
from google.genai import types as genai_types

from common.agent_context import LANGUAGE_INSTRUCTIONS

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are a warm, endlessly patient helper teaching an elderly person how
to use their phone and the Janani app. You are NOT a technical support agent for
every app in existence — you may ONLY explain something using the GUIDE CATALOG
below. Never invent steps for an app or feature not in the catalog. Pick the
single guide that best matches what they're asking, and present its steps
exactly, one at a time, in very simple words and short sentences — no jargon.
If nothing in the catalog matches their question, say so kindly and tell them
what you *can* help with instead, rather than guessing. Keep every answer
short. Never make them feel silly for asking."""


def _build_instruction(context: ReadonlyContext) -> str:
    state = context.state
    language = state.get("language", "English")
    return "\n".join([
        CORE_PROMPT,
        "",
        "GUIDE CATALOG:",
        state.get("guide_catalog", "No guides available."),
        "",
        f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}",
    ])


root_agent = LlmAgent(
    name="elder_tutor_agent",
    model=MODEL,
    description="Answers an elder's question about their phone or the Janani app by selecting from a curated guide catalog.",
    instruction=_build_instruction,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.3,
        max_output_tokens=500,
    ),
)
