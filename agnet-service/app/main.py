"""
Aveline Agent Service — FastAPI Entry Point

This is the main entry point for the Aveline Agentic AI service.
It is a FastAPI application that exposes HTTP endpoints consumed
exclusively by the ASP.NET Core backend (Aveline.Api).

DO NOT call this service directly from React or Flutter.

Routes will be registered here as agents are built.
See app/api/ for route handler modules.
"""

from fastapi import FastAPI

app = FastAPI(
    title="Aveline Agent Service",
    description="LangGraph-powered agentic AI for Boutique Concierge workflows.",
    version="0.1.0",
)

# ---------------------------------------------------------------------------
# Route registration
# Routes will be mounted here as each agent module is built.
# Example:
#   from app.api import customer_memory, visual_insight, commerce
#   app.include_router(customer_memory.router, prefix="/agents/customer-memory")
#   app.include_router(visual_insight.router, prefix="/agents/visual-insight")
#   app.include_router(commerce.router, prefix="/agents/commerce")
# ---------------------------------------------------------------------------


@app.get("/health", tags=["Health"])
async def health_check() -> dict:
    """Basic health check endpoint used by docker-compose and CI."""
    return {"status": "ok", "service": "aveline-agent-service"}
