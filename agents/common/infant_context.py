"""Shared per-request context wrapper for infant-care agents. Mirrors
common/elder_context.py's shape but for an infant profile — age in weeks
and recent feeding/sleep/growth activity instead of age/relation/vitals.
Language uses the same LANGUAGE_INSTRUCTIONS lookup as the other agents.
"""
from typing import Callable

from google.adk.agents.readonly_context import ReadonlyContext

from common.agent_context import LANGUAGE_INSTRUCTIONS

TONE_SUFFIX = """TONE (non-negotiable): Warm, reassuring, and practical. Speak to the
parent/caregiver, not the infant. New parents are often exhausted and anxious —
validate that before anything else. Never be clinical or cold."""


def build_infant_instruction(core_prompt: str) -> Callable[[ReadonlyContext], str]:
    def _instruction(context: ReadonlyContext) -> str:
        state = context.state
        lines = [
            core_prompt,
            "",
            "INFANT CONTEXT:",
            f"- Name: {state.get('infant_name', 'the baby')}",
            f"- Age: {state.get('infant_age_weeks', 'unknown')} weeks",
        ]
        if state.get("recent_activity_summary"):
            lines.append(f"- Recent feeding/sleep activity: {state['recent_activity_summary']}")
        lines.append("")
        language = state.get("language", "English")
        lines.append(f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}")
        lines.append("")
        lines.append(TONE_SUFFIX)
        return "\n".join(lines)

    return _instruction
