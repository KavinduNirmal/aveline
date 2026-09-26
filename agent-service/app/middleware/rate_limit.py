"""Redis ZSET sliding-window rate limiting.

Protects the LLM budget and prevents abuse on expensive agent endpoints. The
window is tracked per client+path in a Redis sorted set keyed by timestamp, so
expired entries can be pruned and the current count read in O(log n).
"""

import logging
import time
from dataclasses import dataclass

import redis.asyncio as aioredis
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request
from starlette.responses import JSONResponse, Response

logger = logging.getLogger("aveline.agent.rate_limit")

KEY_PREFIX = "ratelimit"

# Endpoints that consume LLM budget and are therefore rate-limited. Lightweight
# endpoints (e.g. /health, /search) are intentionally excluded.
RATE_LIMITED_PATHS = ("/agents/query",)


@dataclass
class RateLimitResult:
    """Outcome of a rate-limit check."""

    allowed: bool
    remaining: int
    retry_after: int = 0


class RateLimiter:
    """Sliding-window rate limiter backed by a Redis sorted set."""

    def __init__(self, redis: aioredis.Redis, limit: int = 30, window: int = 60) -> None:
        self._redis = redis
        self.limit = limit
        self.window = window

    def _key(self, scope: str) -> str:
        return f"{KEY_PREFIX}:{scope}"

    async def check(self, scope: str) -> RateLimitResult:
        """Record a request for ``scope`` and report whether it is allowed.

        Args:
            scope: A per-client, per-path identifier (e.g. ``client:1:/agents/query``).

        Returns:
            A ``RateLimitResult`` describing whether the request is allowed, how
            many requests remain in the window, and (when blocked) how many
            seconds to wait before retrying.
        """
        key = self._key(scope)
        now = time.time()
        window_start = now - self.window

        # Prune entries that have fallen out of the window.
        await self._redis.zremrangebyscore(key, 0, window_start)

        # Count current requests in the window.
        count = await self._redis.zcard(key)

        if count >= self.limit:
            oldest = await self._redis.zrange(key, 0, 0, withscores=True)
            retry_after = 0
            if oldest:
                retry_after = max(1, int(self.window - (now - oldest[0][1])))
            return RateLimitResult(allowed=False, remaining=0, retry_after=retry_after)

        # Record this request and bound the key's lifetime to the window.
        await self._redis.zadd(key, {str(now): now})
        await self._redis.expire(key, self.window)
        return RateLimitResult(allowed=True, remaining=self.limit - count - 1)


class RateLimitMiddleware(BaseHTTPMiddleware):
    """Apply sliding-window rate limiting to expensive agent endpoints.

    Only paths under ``RATE_LIMITED_PATHS`` are limited. Allowed responses carry
    ``X-RateLimit-Limit`` / ``X-RateLimit-Remaining`` headers; blocked requests
    return ``429`` with ``X-RateLimit-Reset`` and ``Retry-After``.
    """

    def __init__(
        self,
        app,
        redis: aioredis.Redis,
        limit: int = 30,
        window: int = 60,
        paths: tuple[str, ...] = RATE_LIMITED_PATHS,
    ) -> None:
        super().__init__(app)
        self._limiter = RateLimiter(redis, limit=limit, window=window)
        self._paths = paths

    def _is_limited(self, path: str) -> bool:
        return any(path == p or path.startswith(f"{p}/") for p in self._paths)

    async def dispatch(self, request: Request, call_next) -> Response:
        path = request.url.path
        if not self._is_limited(path):
            return await call_next(request)

        client_host = request.client.host if request.client else "unknown"
        scope = f"{client_host}:{path}"

        try:
            result = await self._limiter.check(scope)
        except Exception:  # noqa: BLE001 - fail open if Redis is unreachable
            logger.warning("Rate limiter unavailable; allowing request.", exc_info=True)
            return await call_next(request)

        if not result.allowed:
            logger.warning(
                "Rate limit exceeded",
                extra={"scope": scope, "limit": self._limiter.limit},
            )
            return JSONResponse(
                status_code=429,
                content={"detail": "Rate limit exceeded. Try again shortly."},
                headers={
                    "X-RateLimit-Limit": str(self._limiter.limit),
                    "X-RateLimit-Remaining": "0",
                    "X-RateLimit-Reset": str(result.retry_after),
                    "Retry-After": str(result.retry_after),
                },
            )

        response = await call_next(request)
        response.headers["X-RateLimit-Limit"] = str(self._limiter.limit)
        response.headers["X-RateLimit-Remaining"] = str(result.remaining)
        return response
