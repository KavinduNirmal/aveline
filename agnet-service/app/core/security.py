import hmac

from fastapi import Header, HTTPException, status

from app.core.config import get_settings

INTERNAL_TOKEN_HEADER = "X-Internal-Token"


async def require_internal_token(
    x_internal_token: str | None = Header(default=None),
) -> None:
    """Reject requests that do not carry the internal service token.

    Only the backend (Aveline.Api) holds this token; the agent service must
    never be called directly by clients.
    """
    settings = get_settings()

    if not settings.internal_api_token:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="INTERNAL_API_TOKEN is not configured.",
        )

    if not hmac.compare_digest(x_internal_token or "", settings.internal_api_token):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid internal token.",
        )
