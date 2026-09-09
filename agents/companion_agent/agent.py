"""Companion Agent â€” conversational RAG pregnancy assistant.

Ported from Services/CompanionService.cs. Unlike Meal/DayNurture, this agent
keeps real conversation memory â€” see main.py, which uses a persistent
DatabaseSessionService (common/session_store.py) instead of a throwaway
per-request session, and passes fresh context via state_delta on every turn
so a long-lived session still reflects the user's current pregnancy week,
mood, etc.
"""
import os

from google.adk.agents import LlmAgent
from google.adk.agents.readonly_context import ReadonlyContext
from google.genai import types as genai_types

from common.agent_context import LANGUAGE_INSTRUCTIONS, TONE_SUFFIX, build_instruction
from companion_agent.knowledge_base import (
    get_bereavement_content,
    get_general_content,
    get_week_content,
)

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

SYSTEM_PROMPT = """You are Janani â€” a warm, knowledgeable pregnancy and maternal care companion for working women.
You provide information, encouragement, and gentle support throughout pregnancy and early motherhood.

IMPORTANT RULES:
- You are NOT a doctor. For any medical question, always recommend consulting
  their healthcare provider. Never diagnose, prescribe, or give specific medical advice.
- Be warm, empathetic, and non-judgmental. Meet her where she is emotionally.
- Keep responses concise â€” 2-4 short paragraphs maximum. She's tired.
- Use simple, clear language. No medical jargon unless explaining a term.
- If she seems distressed, acknowledge her feelings first before information.
- Always end with a gentle affirmation or encouragement.
- If asked about warning signs, always direct her to contact her provider immediately.

You have access to pregnancy information in the context below. Ground your answers
in this content. If you don't have the information, say so and recommend her provider."""

# A full replacement for SYSTEM_PROMPT, not an addition to it -- used only
# when the profile records a loss (state["is_bereaved"]). The ordinary
# pregnancy prompt assumes a healthy, ongoing or recently-delivered
# pregnancy throughout (week milestones, "your baby", cheerful affirmations)
# in a way that would be actively wrong, not just insensitive, to reuse here
# with a sensitivity line stapled on top.
BEREAVEMENT_SYSTEM_PROMPT = """You are Janani, a warm and gentle companion supporting a mother after a pregnancy
or infant loss (stillbirth, neonatal death, late miscarriage, or the loss of one baby in a
multiple birth).

IMPORTANT RULES:
- You are NOT a doctor and NOT a grief counsellor. For any medical or mental-health question,
  always recommend her provider or a real counsellor. Never diagnose, prescribe, or give
  specific medical advice, and never suggest a medication or a dose.
- Never assume a healthy, ongoing pregnancy or a living baby (no week-by-week milestones, no
  "your baby is growing" language) unless the context below tells you a baby also survived.
- Lead with acknowledging her loss and her feelings, every time, before any information.
- Use her baby's name if it's given to you in context, gently and only if it feels natural.
- Be warm, unhurried, and never falsely cheerful. Grief has no timeline -- don't imply she
  should be "over it" or further along than she is.
- Keep responses concise -- 2-4 short paragraphs maximum.
- If she expresses thoughts of self-harm, treat that as urgent: gently but clearly encourage
  her to reach out right now to her provider, a trusted person nearby, or a helpline, and
  offer the helpline named in the context below.

You have access to bereavement information in the context below. Ground your answers in
this content. If you don't have the information, say so and recommend her provider or a
perinatal loss counsellor."""


def _build_bereavement_instruction(context: ReadonlyContext) -> str:
    # Deliberately NOT built on build_instruction() -- that wrapper always
    # injects "Pregnancy week: N" and "Working woman mode", which would leak
    # pregnancy-context assumptions into a reply this is supposed to keep
    # entirely separate from. Name/language/tone are still relevant, so
    # those are rebuilt here rather than dropped.
    state = context.state
    lines = [
        BEREAVEMENT_SYSTEM_PROMPT,
        "",
        "USER CONTEXT:",
        f"- Name: {state.get('user_name', 'Mama')}",
    ]
    lines.append("")
    language = state.get("language", "English")
    lines.append(f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}")
    lines.append("")
    lines.append(TONE_SUFFIX)
    return "\n".join(lines)


def _build_companion_instruction():
    base_instruction = build_instruction(SYSTEM_PROMPT)

    def _instruction(context: ReadonlyContext) -> str:
        state = context.state

        if state.get("is_bereaved", False):
            baby_name = state.get("baby_name")
            is_partial_loss_multiple = state.get("birth_outcome") == "PartialLossMultiple"
            bereavement_context = (
                "BEREAVEMENT CONTEXT"
                + (f" -- baby's name: {baby_name}" if baby_name else "")
                + f":\n{get_bereavement_content(is_partial_loss_multiple)}"
            )
            return f"{_build_bereavement_instruction(context)}\n\nCONTEXT:\n{bereavement_context}"

        week = state.get("pregnancy_week", 20)
        rag_context = (
            f"PREGNANCY KNOWLEDGE BASE â€” Week {week} context:\n"
            f"{get_week_content(week)}\n\n"
            f"GENERAL PREGNANCY INFORMATION:\n"
            f"{get_general_content()}"
        )
        return f"{base_instruction(context)}\n\nCONTEXT:\n{rag_context}"

    return _instruction


root_agent = LlmAgent(
    name="companion_agent",
    model=MODEL,
    description="Conversational RAG pregnancy assistant â€” answers questions grounded in a pregnancy knowledge base.",
    instruction=_build_companion_instruction(),
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.7,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1200,
    ),
)
