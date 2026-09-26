"""Pydantic I/O models for the Commerce Agent (Slice 3 - Lina).

These are the typed inputs/outputs of the commerce sub-graph. They mirror the backend
``Commerce`` DTO and entity contracts (ASP.NET Core). Models forbid extra fields so
schema/contract drifts fail loudly during tests and execution.
"""

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

CommerceStatus = Literal["success", "pending_approval", "rejected", "skipped", "error", "stub"]
ApprovalType = Literal["high_value_order", "discount", "low_margin", "delivery", "sourcing_request"]
PaymentMethodType = Literal["online", "card", "cash", "bank_transfer"]
PaymentStatusType = Literal["pending", "confirmed", "failed", "expired", "refunded"]
CourierStatusType = Literal["planned", "booked", "in_transit", "delivered", "failed"]


class DealItem(BaseModel):
    """An individual line item within a commerce deal/order."""

    model_config = ConfigDict(extra="forbid")

    item_id: str = Field(..., description="Unique product/item identifier.")
    item_name: str = Field(..., min_length=1, max_length=200, description="Display name of the product.")
    quantity: int = Field(default=1, ge=1, description="Quantity ordered.")
    unit_price: float = Field(..., ge=0.0, description="Unit retail price in local currency (LKR).")
    wholesale_cost: float = Field(default=0.0, ge=0.0, description="Unit acquisition cost in local currency (LKR).")
    total_price: float = Field(..., ge=0.0, description="Total line price (unit_price * quantity).")


class DealEvaluation(BaseModel):
    """Profit margin and business rules evaluation for an order."""

    model_config = ConfigDict(extra="forbid")

    subtotal: float = Field(..., ge=0.0, description="Sum of line item totals before discounts.")
    discount_amount: float = Field(default=0.0, ge=0.0, description="Monetary discount applied.")
    total: float = Field(..., ge=0.0, description="Final payable amount after discounts.")
    total_cost: float = Field(default=0.0, ge=0.0, description="Total wholesale cost of goods.")
    margin: float = Field(..., description="Calculated profit margin ratio (e.g. 0.35 for 35%).")
    loyalty_tier: str | None = Field(default=None, description="Customer tier: VIP, Regular, New.")
    applied_discount_percent: float = Field(default=0.0, ge=0.0, le=1.0, description="Discount rate applied.")
    is_auto_approved: bool = Field(default=True, description="True if order passes all business thresholds.")
    requires_approval: bool = Field(default=False, description="True if any threshold or margin limit is breached.")
    triggered_rules: list[str] = Field(default_factory=list, description="Rule codes that triggered approval.")
    flags: list[str] = Field(default_factory=list, description="Human-readable warning notes.")


class PaymentDetails(BaseModel):
    """Payment checkout link and transaction status."""

    model_config = ConfigDict(extra="forbid")

    amount: float | None = Field(default=None, ge=0.0, description="Payable transaction amount.")
    status: PaymentStatusType = Field(default="pending", description="Current status of the payment.")
    url: str | None = Field(default=None, description="Direct customer payment gateway URL.")
    method: PaymentMethodType = Field(default="online", description="Payment method channel.")
    gateway_transaction_id: str | None = Field(default=None, description="External payment gateway reference.")
    expires_at: str | None = Field(default=None, description="ISO-8601 expiration timestamp for payment link.")


class CourierDetails(BaseModel):
    """Courier dispatch and delivery plan details."""

    model_config = ConfigDict(extra="forbid")

    carrier: str | None = Field(default=None, description="Courier service name (PickMe, Uber, In-house).")
    status: CourierStatusType = Field(default="planned", description="Current status of delivery.")
    delivery_address: str | None = Field(default=None, max_length=500, description="Full delivery address.")
    estimated_fee: float | None = Field(default=None, ge=0.0, description="Estimated delivery fee in LKR.")
    tracking_number: str | None = Field(default=None, max_length=100, description="Courier tracking number.")
    estimated_eta: str | None = Field(default=None, description="ISO-8601 estimated delivery arrival time.")


class CommerceAgentInput(BaseModel):
    """Input payload to drive the Commerce Agent sub-graph."""

    model_config = ConfigDict(extra="forbid")

    org_id: str = Field(..., description="Organization/Tenant ID.")
    order_id: str | None = Field(default=None, description="Existing Order ID if already initiated.")
    customer_id: str | None = Field(default=None, description="Customer UUID if known.")
    customer_name: str | None = Field(default=None, description="Customer name.")
    items: list[DealItem] = Field(default_factory=list, description="List of items in the deal.")
    proposed_discount: float = Field(default=0.0, ge=0.0, le=1.0, description="Discount proposed by associate/buyer.")
    delivery_address: str | None = Field(default=None, description="Customer delivery address.")
    channel: str = Field(default="whatsapp", description="Order source channel (whatsapp, in_store, instagram).")


class CommerceAgentOutput(BaseModel):
    """Structured output produced by the Commerce Agent sub-graph.

    Emitted into the shared concierge ``AgentResponse`` envelope under ``commerce``
    and mapped into Lina content blocks by ``build_lina_blocks``.
    """

    model_config = ConfigDict(extra="forbid")

    agent: str = Field(default="commerce", description="Specialist identifier.")
    ran: bool = Field(default=True, description="Whether the commerce sub-graph executed.")
    status: CommerceStatus = Field(..., description="Execution status.")
    summary: str | None = Field(default=None, description="Human-readable deal summary for the Salon.")
    needs_approval: bool = Field(default=False, description="True if workflow is paused for HITL approval.")
    approval_type: str | None = Field(default=None, description="Approval category if paused.")
    approval_reason: str | None = Field(default=None, description="Explanation why approval is required.")
    deal: DealEvaluation | None = Field(default=None, description="Detailed profit margin and rules evaluation.")
    payment: PaymentDetails | None = Field(default=None, description="Generated payment link and status.")
    courier: CourierDetails | None = Field(default=None, description="Delivery schedule and courier assignment.")
    action_required: str | None = Field(default=None, description="Next step prompt for associate or buyer.")
    reason: str | None = Field(default=None, description="Detailed reasoning or error message.")
