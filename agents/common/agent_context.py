"""Shared per-request context wrapper for every Janani agent.

Ported from Services/AgentArchitecture.cs (AgentHelper.Build): every agent
wraps its own core prompt with the same user-context/language/tone
boilerplate, so tone stays consistent no matter which agent answers.
"""
from typing import Callable

from google.adk.agents.readonly_context import ReadonlyContext

LANGUAGE_INSTRUCTIONS = {
    "English": "Please respond in English.",
    "Hindi": "Please respond entirely in Hindi (हिंदी).",
    "Telugu": "Please respond entirely in Telugu (తెలుగు).",
    "Japanese": "Please respond entirely in Japanese (日本語).",
    "Norwegian": "Please respond entirely in Norwegian (Norsk).",
    "Arabic": "Please respond entirely in Arabic (العربية).",
    "Bengali": "Please respond entirely in Bengali (বাংলা).",
    "Marathi": "Please respond entirely in Marathi (मराठी).",
    "Tamil": "Please respond entirely in Tamil (தமிழ்).",
    "Gujarati": "Please respond entirely in Gujarati (ગુજરાતી).",
    "Urdu": "Please respond entirely in Urdu (اردو).",
    "Kannada": "Please respond entirely in Kannada (ಕನ್ನಡ).",
    "Odia": "Please respond entirely in Odia (ଓଡ଼ିଆ).",
    "Malayalam": "Please respond entirely in Malayalam (മലയാളം).",
    "Punjabi": "Please respond entirely in Punjabi (ਪੰਜਾਬੀ).",
}

# Appended to the prompt of every agent that can suggest getting help fast.
# Without it the model volunteers "call 911" -- its US default -- which is wrong
# for an app built for India, and it appeared in every Critical alert including
# the ones emailed to the doctor. Defined once here rather than copied into each
# agent, so the number cannot drift between them.
EMERGENCY_GUIDANCE = """

EMERGENCY NUMBERS: Janani is used in India. If you refer to emergency services,
say 108 (ambulance) or 112 (the all-India emergency number). Never say 911 and
never name a foreign emergency number or service."""


TONE_SUFFIX = """TONE (non-negotiable): Warm, gentle, never prescriptive. Never use "should",
"must", or "you need to". Always meet her where she is. She is doing something
extraordinary — honor that in every word."""


def build_instruction(core_prompt: str) -> Callable[[ReadonlyContext], str]:
    """Equivalent of AgentHelper.Build(corePrompt, ctx) — returns an ADK
    InstructionProvider that wraps core_prompt with USER CONTEXT / LANGUAGE /
    TONE, reading values from session state set on each request."""

    def _instruction(context: ReadonlyContext) -> str:
        state = context.state
        lines = [
            core_prompt,
            "",
            "USER CONTEXT:",
            f"- Name: {state.get('user_name', 'Mama')}",
            f"- Pregnancy week: {state.get('pregnancy_week', 20)}",
            f"- Working woman mode: {state.get('working_woman_mode', True)}",
        ]
        if state.get("last_mood"):
            lines.append(f"- How she's feeling today: {state['last_mood']}")
        lines.append("")
        language = state.get("language", "English")
        lines.append(f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}")
        lines.append("")
        lines.append(TONE_SUFFIX)
        return "\n".join(lines)

    return _instruction
