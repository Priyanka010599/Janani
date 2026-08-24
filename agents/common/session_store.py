"""Persistent session store for stateful agents (Companion).

Local dev: SQLite file via aiosqlite (no setup needed). Cloud Run: Postgres
on the same Cloud SQL instance the .NET app uses, via asyncpg over the
Cloud SQL unix socket mounted by --add-cloudsql-instances. Stateless agents
(Meal, DayNurture) don't use this — they use a throwaway InMemorySessionService
per request, same as before.
"""
import os

from google.adk.sessions import DatabaseSessionService


def build_session_service() -> DatabaseSessionService:
    db_user = os.environ.get("DB_USER")
    db_password = os.environ.get("DB_PASSWORD")
    db_name = os.environ.get("DB_NAME")
    cloudsql_connection = os.environ.get("CLOUDSQL_CONNECTION_NAME")

    if db_user and db_password and db_name and cloudsql_connection:
        db_url = (
            f"postgresql+asyncpg://{db_user}:{db_password}@/{db_name}"
            f"?host=/cloudsql/{cloudsql_connection}"
        )
    else:
        db_url = "sqlite+aiosqlite:///./janani_sessions.db"

    return DatabaseSessionService(db_url=db_url)
