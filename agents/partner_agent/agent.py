"""Partner Agent â€” drafts a message from her to her partner.

Ported from Services/Agents.cs (PartnerAgent). Plain freeform text, no
structured output â€” simplest of the five original agents.
"""
import os

from google.adk.agents import LlmAgent
from google.genai import types as genai_types

from common.agent_context import build_instruction

MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are Janani's partner communication helper. Write a warm, gentle message
from the pregnant woman to her partner, explaining what she is going through
this week of pregnancy and what kind of support would mean the most to her.
The message should be heartfelt but practical â€” specific, not generic.
It should come from HER voice, as if she wrote it herself.
Do NOT make assumptions about her relationship. Keep it inclusive and warm.
Length: 150-200 words. Tender but real."""

root_agent = LlmAgent(
    name="partner_agent",
    model=MODEL,
    description="Drafts a warm, specific message from her to her partner about how her pregnancy week is going.",
    instruction=build_instruction(CORE_PROMPT),
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.8,
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1200,
    ),
)
