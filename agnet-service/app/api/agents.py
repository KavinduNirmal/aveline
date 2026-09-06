import logging
from typing import Annotated

from fastapi import APIRouter, Body, Depends

from app.core.security import require_internal_token

logger = logging.getLogger("aveline.agent.api")

router = APIRouter(
    prefix="/agents",
    tags=["Agents"],
    dependencies=[Depends(require_internal_token)],
)


@router.post("/ping")
async def agents_ping(payload: Annotated[dict | None, Body()] = None) -> dict:
    """Echo endpoint for verifying service-to-service authentication.

    Logs the user context forwarded by the backend.
    """
    payload = payload or {}
    logger.info(
        "Agent ping received: user_id=%s roles=%s",
        payload.get("userId"),
        payload.get("roles"),
        extra={"action": "agent_ping", "user_id": payload.get("userId"), "roles": payload.get("roles")},
    )
    return {"status": "ok", "echo": payload}


@router.post("/warmup")
async def agents_warmup(payload: Annotated[dict | None, Body()] = None) -> dict:
    """Warm up the Customer Memory Agent and operational agents with boutique seed context."""
    payload = payload or {}
    org_id = payload.get("organizationId")
    boutique_name = payload.get("boutiqueName")
    plan_tier = payload.get("planTier")

    logger.info(
        "Agent warmup triggered: org_id=%s boutique=%s tier=%s",
        org_id,
        boutique_name,
        plan_tier,
        extra={
            "action": "agent_warmup",
            "organization_id": org_id,
            "boutique_name": boutique_name,
            "plan_tier": plan_tier,
        },
    )
    return {
        "status": "warmed",
        "organizationId": org_id,
        "ready": True,
        "agents": ["CustomerMemoryAgent", "VisualInsightAgent", "CommerceAgent"],
    }

