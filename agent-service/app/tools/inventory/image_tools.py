"""Image analysis tools for Visual Insight Agent (Elle).

Extracts fashion attributes (category, silhouette, colors, fabric, pattern, occasion)
from garment photographs or inspiration mood boards.

The analysis outcome is **typed**: a caller can tell a *denied* analysis (the backend
refused it: cross-org reference, unknown row, bad request) from a *failed* one (the
backend was unreachable or errored). The old blanket ``except`` collapsed both into a
fabricated "Curated Piece" answer, which is exactly the silent-wrong-answer shape this
unit removes (strategy §5.1 S5).
"""

from dataclasses import dataclass
from enum import StrEnum
from typing import Any

import httpx

from app.schemas.visual_insight import ImageAttributes

#: 4xx statuses that are retryable rather than a refusal of the analysis itself.
_RETRYABLE_CLIENT_STATUSES = frozenset({408, 429})


class AnalysisStatus(StrEnum):
    """The outcome of one vision analysis call."""

    SUCCEEDED = "succeeded"
    DENIED = "denied"
    FAILED = "failed"


@dataclass(frozen=True)
class ImageAnalysisOutcome:
    """A typed vision-analysis result.

    ``DENIED`` means the backend actively refused the request (4xx): the reference did not
    resolve for this organisation, or the request itself was rejected. ``FAILED`` means the
    analysis could not complete (5xx, timeout, connection error). Neither carries fabricated
    attributes.
    """

    status: AnalysisStatus
    attributes: ImageAttributes | None = None
    status_code: int | None = None
    error: str | None = None

    @property
    def succeeded(self) -> bool:
        """Whether the analysis returned parsed attributes."""
        return self.status is AnalysisStatus.SUCCEEDED


def parse_image_attributes_dict(data: dict[str, Any]) -> ImageAttributes:
    """Parse raw image analysis dictionary into validated ImageAttributes."""
    category = data.get("category") or "Garment"
    primary_color = data.get("primary_color") or data.get("color") or "Neutral"
    secondary_colors = data.get("secondary_colors") or []
    if isinstance(secondary_colors, str):
        secondary_colors = [s.strip() for s in secondary_colors.split(",") if s.strip()]

    aesthetic_tags = data.get("aesthetic_tags") or data.get("tags") or []
    if isinstance(aesthetic_tags, str):
        aesthetic_tags = [t.strip() for t in aesthetic_tags.split(",") if t.strip()]

    return ImageAttributes(
        category=str(category),
        silhouette=data.get("silhouette"),
        primary_color=str(primary_color),
        secondary_colors=secondary_colors,
        fabric=data.get("fabric") or data.get("material"),
        pattern=data.get("pattern"),
        occasion=data.get("occasion"),
        aesthetic_tags=aesthetic_tags,
    )


async def analyze_product_image(
    registry: Any,
    image_url: str | None = None,
    *,
    org_id: str | None = None,
    image_ref_kind: str | None = None,
    image_ref_id: str | None = None,
) -> ImageAnalysisOutcome:
    """Analyze a product image, preferring the reference over the legacy URL arm."""
    try:
        res = await registry.analyze_product_image(
            image_url=image_url,
            org_id=org_id,
            image_ref_kind=image_ref_kind,
            image_ref_id=image_ref_id,
        )
    except httpx.HTTPStatusError as exc:
        status_code = exc.response.status_code
        if 400 <= status_code < 500 and status_code not in _RETRYABLE_CLIENT_STATUSES:
            return ImageAnalysisOutcome(
                status=AnalysisStatus.DENIED,
                status_code=status_code,
                error=f"vision backend refused the analysis (HTTP {status_code})",
            )
        return ImageAnalysisOutcome(
            status=AnalysisStatus.FAILED,
            status_code=status_code,
            error=f"vision backend error (HTTP {status_code})",
        )
    except httpx.RequestError as exc:
        return ImageAnalysisOutcome(
            status=AnalysisStatus.FAILED,
            error=f"vision backend unreachable: {exc}",
        )

    data = (res or {}).get("attributes") or res or {}
    return ImageAnalysisOutcome(
        status=AnalysisStatus.SUCCEEDED,
        attributes=parse_image_attributes_dict(data),
    )
