import logging
import os
from contextlib import asynccontextmanager

import redis.asyncio as aioredis
from fastapi import FastAPI

from app.api import agents
from app.core.config import get_settings
from app.core.logging import configure_logging
from app.events.bus import RedisEventBus

logger = logging.getLogger("aveline.agent.main")

configure_logging(log_format=os.getenv("AVELINE_LOG_FORMAT", "json"))

settings = get_settings()


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Start the Redis event bus subscriber on startup and stop it cleanly on shutdown."""
    event_bus: RedisEventBus | None = None
    if settings.redis_url:
        redis = aioredis.from_url(settings.redis_url, decode_responses=True)
        event_bus = RedisEventBus(redis)
        app.state.event_bus = event_bus
        await event_bus.start(settings.subscribe_event_types)
        logger.info("Redis event bus started (subscribing to %s).", settings.subscribe_event_types)
    else:
        logger.warning("REDIS_URL is not configured; Redis event bus is disabled.")

    try:
        yield
    finally:
        if event_bus is not None:
            await event_bus.stop()
            logger.info("Redis event bus stopped.")


app = FastAPI(
    title=settings.app_name,
    description="LangGraph-powered agentic AI for Boutique Concierge workflows.",
    version=settings.version,
    lifespan=lifespan,
)

# Routers under /agents require the internal service token (see app/core/security.py).
app.include_router(agents.router)


@app.get("/health", tags=["Health"])
async def health_check() -> dict:
    """Basic health check used by docker-compose and CI (public)."""
    redis_status = "disabled"
    event_bus = getattr(app.state, "event_bus", None)
    if event_bus is not None:
        redis_status = "ok" if await event_bus.ping() else "unreachable"
    return {"status": "ok", "service": "aveline-agent-service", "redis": redis_status}
