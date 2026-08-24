"""Shared per-request context wrapper for elder-care agents (ElderNurture,
HealthMonitor). Mirrors common/agent_context.py's shape but for an elder
profile instead of a pregnancy profile — the fields don't overlap
(age/relation vs. pregnancy week), so this is a separate helper, not a
variant of the pregnancy one. Language uses the same LANGUAGE_INSTRUCTIONS
lookup as the pregnancy agents rather than a second copy of it.
"""
from typing import Callable

from google.adk.agents.readonly_context import ReadonlyContext

from common.agent_context import LANGUAGE_INSTRUCTIONS

TONE_SUFFIX = """TONE (non-negotiable): Warm, respectful, and practical. Speak to the
caregiver, not the elder. Never be clinical or cold. Acknowledge how much
care and attention this takes."""


def build_elder_instruction(core_prompt: str) -> Callable[[ReadonlyContext], str]:
    def _instruction(context: ReadonlyContext) -> str:
        state = context.state
        lines = [
            core_prompt,
            "",
            "ELDER CONTEXT:",
            f"- Name: {state.get('elder_name', 'them')}",
            f"- Age: {state.get('elder_age', 'unknown')}",
            f"- Relation to caregiver: {state.get('elder_relation', 'family member')}",
        ]
        if state.get("recent_vitals_summary"):
            lines.append(f"- Recent vitals: {state['recent_vitals_summary']}")
        lines.append("")
        language = state.get("language", "English")
        lines.append(f"LANGUAGE: {LANGUAGE_INSTRUCTIONS.get(language, LANGUAGE_INSTRUCTIONS['English'])}")
        lines.append("")
        lines.append(TONE_SUFFIX)
        return "\n".join(lines)

    return _instruction
