"""Reusable async HTTP client for the ASP.NET Core internal API.

Every call attaches the shared ``X-Internal-Token`` header (ADR-009) and targets
``api_base_url``. The agent service never talks to the database or third parties
directly — all business logic lives behind these internal endpoints.
"""

import logging
from typing import Any

import httpx

from app.core.config import get_settings

logger = logging.getLogger("aveline.agent.tools.client")


class InternalApiClient:
    """Async client for backend internal endpoints with service-to-service auth."""

    def __init__(
        self,
        base_url: str | None = None,
        internal_token: str | None = None,
        timeout: float = 30.0,
    ) -> None:
        settings = get_settings()
        self._base_url = (base_url or settings.api_base_url).rstrip("/")
        self._internal_token = internal_token or settings.internal_api_token
        self._timeout = timeout

    async def request(
        self,
        method: str,
        path: str,
        json: dict[str, Any] | None = None,
    ) -> Any:
        """Perform an authenticated request and return the parsed JSON body.

        Args:
            method: HTTP method (``GET``, ``POST``, ...).
            path: Endpoint path, e.g. ``/api/internal/customers/{id}``.
            json: Optional JSON body for POST/PUT/PATCH.

        Returns:
            The parsed JSON response body.

        Raises:
            httpx.HTTPStatusError: If the backend returns a non-2xx status.
            httpx.RequestError: On network-level failures.
        """
        headers = {"X-Internal-Token": self._internal_token}
        url = f"{self._base_url}{path}"

        async with httpx.AsyncClient(timeout=self._timeout) as http:
            response = await http.request(method, url, headers=headers, json=json)
            response.raise_for_status()
            if response.content:
                return response.json()
            return None
