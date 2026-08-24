"""Meal Agent â€” pregnancy-safe meal suggestions.

Ported from Janani/Services/Agents.cs (MealAgent). Same core prompt and
the same structured-output shape (opening/meals/gentleNote) the .NET
Meals.razor page already expects â€” only the transport changes: this now
runs as a real ADK LlmAgent calling Gemini via Vertex AI, instead of the
.NET app's OpenAI-SDK-against-the-Gemini-OpenAI-compat-endpoint call.
"""
import os
from typing import List

from google.adk.agents import LlmAgent
from google.genai import types as genai_types
from pydantic import BaseModel, Field

from common.agent_context import build_instruction

# Same env var name the .NET side already uses (Program.cs /
# deploy-cloudrun.ps1), so both services stay pointed at the same model
# without needing two separate settings. Default updated from the .NET
# side's stale "gemini-1.5-flash" (confirmed 404, retired). gemini-2.5-flash
# is also retired for new callers; the API's own error pointed at
# gemini-2.5-flash as current. See chat for the live-app fix needed too.
MODEL = os.environ.get("VERTEX_CHAT_DEPLOYMENT", "gemini-2.5-flash")

CORE_PROMPT = """You are Janani's meal nurturer. Suggest pregnancy-safe, gentle meal ideas
based on how she is feeling. All meals must be safe during pregnancy.
If she is nauseous, suggest bland, gentle foods that are easy to keep down.
If she is hungry/energetic, suggest nourishing, satisfying options.
Never suggest anything not safe in pregnancy (raw fish, unpasteurized dairy, etc.).
Keep descriptions warm and appetizing, never clinical."""


class MealItem(BaseModel):
    name: str
    description: str
    whyItHelps: str


class MealSuggestion(BaseModel):
    opening: str
    meals: List[MealItem] = Field(min_length=3, max_length=4)
    gentleNote: str


root_agent = LlmAgent(
    name="meal_agent",
    model=MODEL,
    description="Suggests gentle, pregnancy-safe meals based on how she's feeling.",
    instruction=build_instruction(CORE_PROMPT),
    output_schema=MealSuggestion,
    generate_content_config=genai_types.GenerateContentConfig(
        temperature=0.8,
        # gemini-2.5-flash spends output-token budget on hidden "thinking"
        # tokens by default; for a simple structured-output task that was
        # consuming the entire 450-token budget before any JSON came out.
        # thinking_budget=0 is rejected for this model (400), so keep a
        # small nonzero budget and give the response itself plenty of room.
        thinking_config=genai_types.ThinkingConfig(thinking_budget=128),
        max_output_tokens=1200,
    ),
)
