"""Interactive Demo for the Commerce Agent (Lina - Slice 3).

Demonstrates:
  1. An Auto-Approved Deal (within threshold, margin OK -> payment link & courier booked).
  2. A High-Value Deal (exceeds LKR 40,000 -> triggers Human-in-the-Loop approval pause).
  3. Resume Workflow (store owner approves high-value order -> payment link issued).
"""

import asyncio
import json
import sys

# Ensure UTF-8 console output on Windows
if sys.stdout.encoding != "utf-8":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

from app.agents.commerce.graph import build_commerce_graph
from app.events.block_builders import build_lina_blocks


async def main():
    print("=" * 70)
    print(" [AVELINE COMMERCE AGENT (LINA) - INTERACTIVE DEMO]")
    print("=" * 70)

    graph = build_commerce_graph(registry=None)
    org_id = "org-boutique-colombo"

    # ---------------------------------------------------------------------------
    # SCENARIO 1: Auto-Approved Deal
    # ---------------------------------------------------------------------------
    print("\n[SCENARIO 1] Inbound Deal: 2 Linen Tops (LKR 16,000, 5% VIP Discount)")
    print("-" * 70)

    deal_1 = {
        "org_id": org_id,
        "order_id": "ord-101",
        "customer_id": "cust-01",
        "customer_name": "Eleanor Vance",
        "items": [
            {
                "item_id": "prod-1",
                "item_name": "Italian Linen Top",
                "quantity": 2,
                "unit_price": 8000.0,
                "wholesale_cost": 4200.0,
                "total_price": 16000.0,
            }
        ],
        "proposed_discount": 0.05,
        "delivery_address": "45 Ward Place, Colombo 07",
        "channel": "whatsapp",
        "message": "Hi Lina, can I get these 2 linen tops with my 5% VIP discount?",
    }

    res_1 = await graph.ainvoke(deal_1)
    output_1 = res_1["output"]

    print(f"Status:            {output_1['status'].upper()} (Auto-Approved)")
    print(f"Subtotal:          LKR {output_1['deal']['subtotal']:,.2f}")
    print(f"Discount Applied:  LKR {output_1['deal']['discount_amount']:,.2f} ({output_1['deal']['applied_discount_percent']:.0%})")
    print(f"Final Total:       LKR {output_1['deal']['total']:,.2f}")
    print(f"Profit Margin:     {output_1['deal']['margin']:.1%}")
    print(f"Payment Link:      {output_1['payment']['url']}")
    print(f"Courier:           {output_1['courier']['carrier']} -> {output_1['courier']['delivery_address']} (Fee: LKR {output_1['courier']['estimated_fee']})")
    print(f"Lina Summary:      {output_1['summary']}")

    print("\nRendered Salon UI Content Blocks for Lina:")
    blocks_1 = build_lina_blocks(output_1)
    print(json.dumps(blocks_1, indent=2))

    # ---------------------------------------------------------------------------
    # SCENARIO 2: High-Value Deal Requiring Owner Approval (HITL)
    # ---------------------------------------------------------------------------
    print("\n" + "=" * 70)
    print("[SCENARIO 2] High-Value Deal: Designer Evening Gown (LKR 75,000)")
    print("-" * 70)

    deal_2 = {
        "org_id": org_id,
        "order_id": "ord-202",
        "customer_id": "cust-02",
        "customer_name": "Sophia Sterling",
        "items": [
            {
                "item_id": "prod-2",
                "item_name": "Hand-Embroidered Evening Gown",
                "quantity": 1,
                "unit_price": 75000.0,
                "wholesale_cost": 42000.0,
                "total_price": 75000.0,
            }
        ],
        "proposed_discount": 0.0,
        "delivery_address": "Galle Face Court, Colombo 03",
        "channel": "in_store",
        "message": "Customer wishes to purchase the showroom gown.",
    }

    res_2 = await graph.ainvoke(deal_2)
    output_2 = res_2["output"]

    print(f"Status:                 {output_2['status'].upper()}")
    print(f"Requires Human Review:  {output_2['needs_approval']}")
    print(f"Approval Type:          {output_2['approval_type']}")
    print(f"Triggered Rules:        {output_2['deal']['triggered_rules']}")
    print(f"Warning Flag:           {output_2['deal']['flags'][0]}")
    print(f"Workflow State:         {output_2['summary']}")

    # ---------------------------------------------------------------------------
    # SCENARIO 3: Store Owner Approves via Web Dashboard
    # ---------------------------------------------------------------------------
    print("\n" + "=" * 70)
    print("[SCENARIO 3] Store Owner Reviews & Approves Order on Web Dashboard")
    print("-" * 70)

    resumed_deal_2 = dict(deal_2)
    resumed_deal_2["approval_decision"] = "approved"
    resumed_deal_2["approval_comment"] = "Approved by Store Owner"

    res_3 = await graph.ainvoke(resumed_deal_2)
    output_3 = res_3["output"]

    print(f"Status After Sign-Off:  {output_3['status'].upper()}")
    print(f"Payment Link Generated: {output_3['payment']['url']}")
    print(f"Courier Assigned:       {output_3['courier']['carrier']} (Fee: LKR {output_3['courier']['estimated_fee']})")
    print(f"Final Salon Message:    {output_3['summary']}")

    print("\n" + "=" * 70)
    print("DEMO COMPLETE: All Commerce Agent flows verified successfully!")
    print("=" * 70)


if __name__ == "__main__":
    asyncio.run(main())
