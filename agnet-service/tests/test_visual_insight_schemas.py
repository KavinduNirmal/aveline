import pytest
from pydantic import ValidationError

from app.schemas.visual_insight import (
    ImageAttributes,
    LookDto,
    PieceItem,
    SourcingRequestDto,
    VisualAgentOutput,
)


def test_image_attributes_valid():
    attrs = ImageAttributes(
        category="Gown",
        silhouette="A-line",
        primary_color="Peach",
        secondary_colors=["Gold", "Ivory"],
        fabric="Raw Silk",
        occasion="Wedding",
        aesthetic_tags=["Bespoke", "Quiet Luxury"],
    )
    assert attrs.category == "Gown"
    assert attrs.primary_color == "Peach"
    assert "Gold" in attrs.secondary_colors


def test_image_attributes_forbids_extra():
    with pytest.raises(ValidationError):
        ImageAttributes(
            category="Dress",
            primary_color="Red",
            extra_field="invalid",  # type: ignore[call-arg]
        )


def test_piece_item_serialization():
    item = PieceItem(
        itemId="item-123",
        name="Peach Raw-Silk Drape Gown",
        price=1250.00,
        size="UK 10",
        stock=2,
        imageUrl="https://images.aveline.luxury/gown1.jpg",
    )
    dumped = item.model_dump()
    assert dumped["itemId"] == "item-123"
    assert dumped["stock"] == 2


def test_look_dto():
    item = PieceItem(itemId="item-1", name="Silk Blouse", price=450.0)
    look = LookDto(
        name="Riviera Evening",
        text="A flowing look paired with tailored raw-silk trousers.",
        imageUrl="https://images.aveline.luxury/look1.jpg",
        items=[item],
    )
    assert look.name == "Riviera Evening"
    assert len(look.items) == 1


def test_visual_agent_output_contract():
    item = PieceItem(itemId="i-1", name="Embroidered Kimono", price=890.0)
    look = LookDto(name="Morning Lounge", text="Effortless morning grace.", items=[item])
    sourcing = SourcingRequestDto(
        requestId="src-001",
        customerId="cust-42",
        imageUrl="https://pinterest.com/pin/123.jpg",
        notes="Customer seeks discontinued emerald shade.",
    )
    out = VisualAgentOutput(
        status="success",
        suggestion="I have curated 1 signature piece and a complete look for your gala.",
        items=[item],
        looks=[look],
        sourcing_request=sourcing,
    )
    assert out.status == "success"
    assert out.agent == "visual"
    assert out.ran is True
    assert len(out.items) == 1
    assert out.sourcing_request is not None
    assert out.sourcing_request.status == "pending"


# ---------------------------------------------------------------------------
# Output Schema Definitive Contract Tests
# ---------------------------------------------------------------------------


def test_visual_output_schema_accepts_valid_response():
    result = VisualAgentOutput(
        status="success",
        found_items=[
            {
                "item_id": "123",
                "price": 45000,
            }
        ],
    )

    assert result.status == "success"
    assert len(result.found_items) == 1
    assert result.found_items[0].item_id == "123"
    assert result.found_items[0].price == 45000


def test_visual_output_schema_rejects_invalid_status():
    with pytest.raises(ValidationError):
        VisualAgentOutput(
            status="invalid_status_value",  # type: ignore[arg-type]
            found_items=[],
        )


def test_visual_output_schema_rejects_incorrect_match_confidence():
    # match_confidence must be between 0.0 and 1.0
    with pytest.raises(ValidationError):
        VisualAgentOutput(
            status="success",
            found_items=[
                {
                    "item_id": "123",
                    "price": 45000,
                    "match_confidence": 1.5,  # Invalid: > 1.0
                }
            ],
        )

    with pytest.raises(ValidationError):
        VisualAgentOutput(
            status="success",
            found_items=[
                {
                    "item_id": "123",
                    "price": 45000,
                    "match_confidence": -0.2,  # Invalid: < 0.0
                }
            ],
        )


def test_visual_output_schema_rejects_invalid_monetary_value():
    # Monetary values cannot be negative or unparseable strings
    with pytest.raises(ValidationError):
        VisualAgentOutput(
            status="success",
            found_items=[
                {
                    "item_id": "123",
                    "price": -500.0,  # Invalid: negative price
                }
            ],
        )

    with pytest.raises(ValidationError):
        VisualAgentOutput(
            status="success",
            found_items=[
                {
                    "item_id": "123",
                    "price": "not_a_valid_amount",  # type: ignore[typeddict-item]
                }
            ],
        )


def test_visual_output_schema_rejects_incorrect_field_type():
    with pytest.raises(ValidationError):
        VisualAgentOutput(
            status="success",
            found_items="not_a_list",  # type: ignore[arg-type]
        )


def test_visual_output_schema_rejects_malformed_output():
    with pytest.raises(ValidationError):
        # Missing required 'status' field
        VisualAgentOutput.model_validate({"malformed": True})

