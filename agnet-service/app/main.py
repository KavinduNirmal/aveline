import logging

from fastapi import FastAPI

from app.api import agents
from app.core.config import get_settings

logging.basicConfig(level=logging.INFO)

settings = get_settings()

app = FastAPI(
    title=settings.app_name,
    description="LangGraph-powered agentic AI for Boutique Concierge workflows.",
    version=settings.version,
)

# Routers under /agents require the internal service token (see app/core/security.py).
app.include_router(agents.router)


@app.get("/health", tags=["Health"])
async def health_check() -> dict:
    """Basic health check used by docker-compose and CI (public)."""
    return {"status": "ok", "service": "aveline-agent-service"}
