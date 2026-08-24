"""Shared ADK runner invocation for stateless, single-turn agents.

Every current agent (Meal, DayNurture, ...) has no cross-request memory in
the original C# implementation either — a fresh ChatClient call every time
— so each request gets its own throwaway session. Agents that need real
conversation memory (the Companion port) will use a persistent
SessionService and a different call shape, not this helper.
"""
import uuid
from typing import Any, AsyncIterator, Optional

from google.adk.agents.run_config import RunConfig, StreamingMode
from google.adk.runners import Runner
from google.adk.sessions import BaseSessionService
from google.genai import types as genai_types


async def run_single_turn(
    runner: Runner,
    session_service: BaseSessionService,
    app_name: str,
    state: dict[str, Any],
    message_text: str,
    user_id: str = "single-turn",
) -> Optional[str]:
    session_id = str(uuid.uuid4())
    await session_service.create_session(
        app_name=app_name, user_id=user_id, session_id=session_id, state=state
    )

    message = genai_types.Content(role="user", parts=[genai_types.Part(text=message_text)])

    final_text = None
    async for event in runner.run_async(user_id=user_id, session_id=session_id, new_message=message):
        if event.is_final_response() and event.content and event.content.parts:
            final_text = event.content.parts[0].text
    return final_text


async def run_persisted_turn(
    runner: Runner,
    session_service: BaseSessionService,
    app_name: str,
    user_id: str,
    session_id: str,
    state_delta: dict[str, Any],
    message_text: str,
) -> Optional[str]:
    """Non-streaming counterpart to stream_turn, for agents with real
    persisted memory whose output must be parsed whole rather than streamed
    (structured JSON, e.g. InfantDailyPlan/ElderDailyPlan) — same
    reuse/create-session behavior, just returns the complete final text.
    """
    existing = await session_service.get_session(
        app_name=app_name, user_id=user_id, session_id=session_id
    )
    if existing is None:
        await session_service.create_session(
            app_name=app_name, user_id=user_id, session_id=session_id, state=state_delta
        )

    message = genai_types.Content(role="user", parts=[genai_types.Part(text=message_text)])

    final_text = None
    async for event in runner.run_async(
        user_id=user_id, session_id=session_id, new_message=message, state_delta=state_delta
    ):
        if event.is_final_response() and event.content and event.content.parts:
            final_text = event.content.parts[0].text
    return final_text


async def stream_turn(
    runner: Runner,
    session_service: BaseSessionService,
    app_name: str,
    user_id: str,
    session_id: str,
    state_delta: dict[str, Any],
    message_text: str,
) -> AsyncIterator[str]:
    """For agents with real persisted memory (Companion): reuses/creates a
    stable session per (app_name, user_id, session_id) rather than a fresh
    one per request, and yields incremental text deltas as they arrive.

    ADK streaming events come in two shapes for plain-text output: several
    `partial=True` events each carrying only the NEW delta text, followed by
    one final `partial=False` event carrying the FULL cumulative text again.
    Only the partial deltas are yielded here — re-yielding the final event
    too would duplicate everything already streamed.
    """
    existing = await session_service.get_session(
        app_name=app_name, user_id=user_id, session_id=session_id
    )
    if existing is None:
        await session_service.create_session(
            app_name=app_name, user_id=user_id, session_id=session_id, state=state_delta
        )

    message = genai_types.Content(role="user", parts=[genai_types.Part(text=message_text)])
    run_config = RunConfig(streaming_mode=StreamingMode.SSE)

    async for event in runner.run_async(
        user_id=user_id,
        session_id=session_id,
        new_message=message,
        state_delta=state_delta,
        run_config=run_config,
    ):
        if event.partial and event.content and event.content.parts:
            text = event.content.parts[0].text
            if text:
                yield text
