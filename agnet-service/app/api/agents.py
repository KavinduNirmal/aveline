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
