"""Health endpoints for the agent service.

``/health`` is a public liveness probe; ``/health/ready`` is a readiness probe
that verifies database connectivity for CI/CD and orchestrators.
"""

import logging

from fastapi import APIRouter, Response, status

from app.db.connection import check_db_connection

logger = logging.getLogger("aveline.agent.health")

router = APIRouter(tags=["Health"])


@router.get("/health/ready")
async def readiness_check(response: Response) -> dict:
    """Return 200 when the service is ready to serve, else 503."""
    try:
        await check_db_connection()
    except Exception:  # noqa: BLE001 - readiness probe must not raise
        logger.warning("Readiness check failed: database unreachable.")
        response.status_code = status.HTTP_503_SERVICE_UNAVAILABLE
        return {"status": "not_ready", "service": "aveline-agent-service"}
    return {"status": "ready", "service": "aveline-agent-service"}
