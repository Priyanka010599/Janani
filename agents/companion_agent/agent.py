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

from common.agent_context import build_instruction
from companion_agent.knowledge_base import get_general_content, get_week_content

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


def _build_companion_instruction():
    base_instruction = build_instruction(SYSTEM_PROMPT)

    def _instruction(context: ReadonlyContext) -> str:
        week = context.state.get("pregnancy_week", 20)
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
