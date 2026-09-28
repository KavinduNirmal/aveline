#!/usr/bin/env python3
"""Trigger the Human-in-the-Loop approval pause and inspect what the workflow does.

Runs against the agent service directly, so it needs no staff session - only the internal token
the API and agent already share. It exercises the three business-rule breaches that make Lina
(the Commerce agent) stop for sign-off, plus a control case that should NOT pause.

    scripts/hitl-scenario.py                 # run every scenario
    scripts/hitl-scenario.py high_value      # run one

What each scenario needs:
  * `items` in org_context. The conversation path never supplies these - they are order context,
    which is why a chat message alone cannot reach the approval path.
  * A breach of an active business rule. The local evaluator mirrors the .NET defaults:
    high-value > LKR 40,000, margin < 25%, discount above the tier cap (Regular 5%, VIP 10%).
"""

import json
import os
import sys
import urllib.error
import urllib.request

AGENT_URL = os.environ.get("AGENT_URL", "http://localhost:8000")
ORG_ID = os.environ.get("ORG_ID", "")


def internal_token() -> str:
    token = os.environ.get("INTERNAL_API_TOKEN")
    if token:
        return token
    try:
        with open(".env", encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("INTERNAL_API_TOKEN="):
                    return line.split("=", 1)[1].strip()
    except FileNotFoundError:
        pass
    raise SystemExit(
        "No internal token. Set INTERNAL_API_TOKEN, or run from the repo root so .env is readable."
    )


def organization_id() -> str:
    if ORG_ID:
        return ORG_ID
    # The agent resolves nothing itself; the caller supplies the tenant.
    import subprocess

    try:
        out = subprocess.run(
            ["docker", "exec", "aveline_postgres", "psql", "-U", "aveline", "-d", "aveline", "-tAc",
             'SELECT "Id" FROM "Organizations" ORDER BY "CreatedAt" LIMIT 1;'],
            capture_output=True, text=True, timeout=20,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()
    except Exception:  # noqa: BLE001 - fall through to the error below
        pass
    raise SystemExit("Could not determine the organization id. Set ORG_ID.")


#: name -> (item, proposed discount, what the scenario is for)
SCENARIOS: dict[str, dict] = {
    "high_value": {
        "why": "Order total above the high-value threshold (LKR 40,000 default).",
        "items": [{"unit_price": 50000, "quantity": 1, "wholesale_cost": 20000}],
        "discount": 0.0,
        "expect": "pending_approval / high_value_order",
    },
    "low_margin": {
        "why": "Margin below the 25% minimum, while staying under the high-value threshold.",
        "items": [{"unit_price": 10000, "quantity": 1, "wholesale_cost": 9000}],
        "discount": 0.0,
        "expect": "pending_approval / low_margin",
    },
    "discount_cap": {
        "why": "Discount above the Regular tier cap (5%).",
        "items": [{"unit_price": 10000, "quantity": 1, "wholesale_cost": 5000}],
        "discount": 0.10,
        "expect": "pending_approval / discount",
    },
    "auto_approved": {
        "why": "Control: comfortably within every rule, so the workflow must NOT pause.",
        "items": [{"unit_price": 20000, "quantity": 1, "wholesale_cost": 5000}],
        "discount": 0.0,
        "expect": "settles without approval",
    },
}


def run(name: str, token: str, org: str) -> dict:
    scenario = SCENARIOS[name]
    body = json.dumps(
        {
            # "discount" is a pricing signal, which is what routes the plan to the commerce agent.
            "query": "Can the customer get a discount on this order?",
            "thread_id": f"hitl-scenario-{name}",
            "org_context": {
                "organization_id": org,
                "items": scenario["items"],
                "proposed_discount": scenario["discount"],
            },
        }
    ).encode()

    request = urllib.request.Request(
        f"{AGENT_URL}/agents/query",
        data=body,
        headers={"Content-Type": "application/json", "X-Internal-Token": token},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as exc:
        return {"error": f"HTTP {exc.code}: {exc.read().decode()[:300]}"}
    except Exception as exc:  # noqa: BLE001
        return {"error": f"{type(exc).__name__}: {exc}"}


def report(name: str, payload: dict) -> None:
    scenario = SCENARIOS[name]
    print(f"\n{'=' * 78}\n{name}: {scenario['why']}\n  expected: {scenario['expect']}\n{'=' * 78}")

    if "error" in payload:
        print(f"  FAILED: {payload['error']}")
        return

    output = (payload.get("result") or {}).get("output") or {}
    commerce = output.get("commerce") or {}

    print(f"  envelope status : {(payload.get('result') or {}).get('status')}")
    print(f"  intent          : {output.get('intent')}")
    print(f"  agents that ran : memory={'yes' if output.get('memory') else 'no'}"
          f"  visual={'yes' if output.get('visual') else 'no'}"
          f"  commerce={'yes' if commerce else 'no'}")

    if not commerce:
        print("  commerce        : DID NOT RUN (nothing enforces the rule)")
        return

    print(f"  commerce status : {commerce.get('status')}")
    print(f"  needs_approval  : {commerce.get('needs_approval')}")
    print(f"  approval_type   : {commerce.get('approval_type')}")
    print(f"  triggered rules : {(commerce.get('deal') or {}).get('triggered_rules')}")
    print(f"  reason          : {commerce.get('approval_reason')}")
    print(f"  action required : {commerce.get('action_required')}")


def main() -> int:
    token = internal_token()
    org = organization_id()

    requested = sys.argv[1:] or list(SCENARIOS)
    unknown = [name for name in requested if name not in SCENARIOS]
    if unknown:
        raise SystemExit(f"Unknown scenario(s): {', '.join(unknown)}. Try: {', '.join(SCENARIOS)}")

    print(f"org={org}  agent={AGENT_URL}")
    for name in requested:
        report(name, run(name, token, org))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
