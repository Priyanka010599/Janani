"""Recipe Agent — answers a caregiver's free-text recipe request (e.g. "a
first food for my 7 month old", "something for postpartum recovery") by
selecting from a curated, real recipe catalog — never inventing a recipe
of its own. Feeding a newborn or a postpartum body isn't something to let
an LLM improvise; the catalog (injected into context, same RAG-by-
injection approach caregiver_agent uses for scheme content) is the single
source of truth, and the agent's only job is to pick from it and explain.
"""
import os

from google.adk.agents import LlmAgent
from google.adk.agents.readonly_context import ReadonlyContext
from google.genai import types as genai_types

from common.agent_context import LANGUAGE_INSTRUCTIONS

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are a warm assistant helping a new or expecting mother find a
recipe. You are NOT a nutritionist or doctor — never invent a recipe, and
never suggest anything outside the RECIPE CATALOG below. Pick the one or
two recipes from the catalog that best match what she's asking for, and
present their ingredients and instructions clearly, mentioning which
stage/category they're for. If nothing in the catalog is a good fit,
say so honestly and suggest the closest available category rather than
making something up. Always close with a brief reminder to confirm any
new food with her pediatrician, especially around allergies."""


def _build_instruction(context: ReadonlyContext) -> str:
    state = context.state
    language = state.get("language", "English")
    return "\n".join([
        CORE_PROMPT,
        "",
        "RECIPE CATALOG:",
        state.get("recipe_catalog", "No recipes available."),
        "",
        f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}",
    ])


root_agent = LlmAgent(
    name="recipe_agent",
    model=MODEL,
    description="Answers a free-text recipe request by selecting from a curated recipe catalog.",
    instruction=_build_instruction,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.4,
        max_output_tokens=700,
    ),
)
