#!/usr/bin/env python3
"""Drive the whole HITL loop against the running stack, through the real services.

Unlike ``scripts/hitl-scenario.py`` (which talks to the agent service directly, so it can only show
that the *pause* works), this script exercises the seams that ADR-024 is about:

  1. a **signed Meta webhook** kicks off the inbound path, so the API derives the line items from the
     message, calls the agent, and - when the agent pauses - creates the Order and the
     ApprovalQueueEntry itself;
  2. the order and approval rows are then read back out of PostgreSQL, so "it paused" is checked
     against persisted state rather than a log line;
  3. with ``--decide``, the pause is settled through the agent service's ``/agents/resume``, which is
     the endpoint ``ApprovalService`` posts to once an owner decides in the dashboard.

    WHATSAPP_APP_SECRET=<app secret> scripts/hitl-live-check.py --message "I want to buy the emerald green saree, please send the order"
    WHATSAPP_APP_SECRET=... scripts/hitl-live-check.py --decide approve --thread <thread-id>
    WHATSAPP_APP_SECRET=... scripts/hitl-live-check.py --decide reject  --thread <thread-id>

The app secret is read from the environment and never written down: it is the Meta value stored
encrypted in ``IntegrationCredentials``, and it is only needed to sign the local webhook.

Why the resume goes through ``docker run``: the agent service listens on the compose network and its
port is deliberately not published to the host.
"""

from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from uuid import uuid4

DEFAULT_API = os.environ.get("API_URL", "http://localhost:5091")
DEFAULT_ORG = os.environ.get("ORG_ID", "")
DEFAULT_PHONE = os.environ.get("TEST_PHONE", "94763475058")
COMPOSE_NETWORK = os.environ.get("COMPOSE_NETWORK", "aveline_default")
AGENT_IN_CONTAINER = os.environ.get("AGENT_IN_CONTAINER", "http://agent:8000")
POSTGRES_CONTAINER = os.environ.get("POSTGRES_CONTAINER", "aveline_postgres")


def psql(sql: str) -> str:
    """Run one query and return unaligned output."""
    result = subprocess.run(
        [
            "docker", "exec", POSTGRES_CONTAINER,
            "psql", "-U", "aveline", "-d", "aveline", "-tAc", sql,
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise SystemExit(f"psql failed: {result.stderr.strip()}")
    return result.stdout.strip()


def organization_id() -> str:
    if DEFAULT_ORG:
        return DEFAULT_ORG
    value = psql('select "Id" from "Organizations" order by "CreatedAt" limit 1;')
    if not value:
        raise SystemExit("No organization found; set ORG_ID.")
    return value


def sign(body: bytes, secret: str) -> str:
    return "sha256=" + hmac.new(secret.encode(), body, hashlib.sha256).hexdigest()


def webhook_body(org_id: str, phone: str, text: str) -> bytes:
    return json.dumps(
        {
            "object": "whatsapp_business_account",
            "entry": [
                {
                    "id": org_id,
                    "changes": [
                        {
                            "field": "messages",
                            "value": {
                                "messaging_product": "whatsapp",
                                "metadata": {"phone_number_id": "local"},
                                "messages": [
                                    {
                                        "id": f"wamid.{uuid4().hex}",
                                        "from": phone,
                                        "timestamp": str(int(time.time())),
                                        "type": "text",
                                        "text": {"body": text},
                                    }
                                ],
                            },
                        }
                    ],
                }
            ],
        }
    ).encode()


def send(api_url: str, org_id: str, body: bytes, secret: str) -> int:
    request = urllib.request.Request(
        f"{api_url}/api/v1/webhooks/whatsapp/{org_id}",
        data=body,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "X-Hub-Signature-256": sign(body, secret),
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def resume(payload: dict, token: str) -> dict:
    """Settle a paused run through the agent service, from inside the compose network."""
    body = json.dumps(payload)
    result = subprocess.run(
        [
            "docker", "run", "--rm",
            "--network", COMPOSE_NETWORK,
            "curlimages/curl:latest", "-sS",
            "-X", "POST", f"{AGENT_IN_CONTAINER}/agents/resume",
            "-H", "Content-Type: application/json",
            "-H", f"X-Internal-Token: {token}",
            "-d", body,
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise SystemExit(f"resume call failed: {result.stderr.strip()}")
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        raise SystemExit(f"resume returned non-JSON: {result.stdout[:400]}") from None


def internal_token() -> str:
    token = os.environ.get("INTERNAL_API_TOKEN")
    if token:
        return token
    with open(".env", encoding="utf-8") as handle:
        for line in handle:
            if line.startswith("INTERNAL_API_TOKEN="):
                return line.split("=", 1)[1].strip()
    raise SystemExit("No internal token in the environment or .env.")


def show_pause(org_id: str, phone: str) -> dict[str, str] | None:
    conversation = psql(
        f"""select "Id" from "Conversations"
            where "OrganizationId" = '{org_id}' and "ExternalRef" = '{phone}'
            order by "LastMessageAt" desc nulls last limit 1;"""
    )
    if not conversation:
        print("  conversation : (none)")
        return None

    print(f"  conversation : {conversation}")
    order = psql(
        f"""select "Id", "Status", "Subtotal", "Total", "TotalCost", "Margin", "CustomerName"
            from "Orders" where "Id" in (
                select "OrderId" from "ApprovalQueue"
                where "OrganizationId" = '{org_id}' and "ConversationId" = '{conversation}')
            order by "CreatedAt" desc limit 1;"""
    )
    if not order:
        print("  order        : (none — the run did not pause)")
        return None

    order_id, status, subtotal, total, cost, margin, customer_name = order.split("|")
    items = psql(
        f"""select string_agg("ItemName" || ' x' || "Quantity" || ' @ ' || "UnitPrice", ', ')
            from "OrderItems" where "OrderId" = '{order_id}';"""
    )
    approval = psql(
        f"""select "Id", "Status", "ApprovalType", "ThreadId"
            from "ApprovalQueue" where "OrderId" = '{order_id}' limit 1;"""
    )
    approval_id, approval_status, approval_type, thread = (approval.split("|") + [""] * 4)[:4]
    bound = psql(f"""select coalesce("CustomerId"::text, '(unbound)') from "Conversations"
                     where "Id" = '{conversation}';""")

    print(f"  order        : {order_id} status={status}")
    print(f"  items        : {items}")
    print(f"  money        : subtotal={subtotal} total={total} cost={cost} margin={margin}")
    print(f"  customer     : {customer_name}  (conversation bound to {bound})")
    print(f"  approval     : {approval_id} status={approval_status} type={approval_type}")
    print(f"  thread       : {thread}")
    return {
        "thread": thread,
        "order_id": order_id,
        "customer_name": customer_name,
        "approval_id": approval_id,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--message", default="I want to buy the emerald green saree, please send the order")
    parser.add_argument("--phone", default=DEFAULT_PHONE)
    parser.add_argument("--api", default=DEFAULT_API)
    parser.add_argument("--decide", choices=["approve", "reject", "revise"], default=None)
    parser.add_argument("--revised-discount", type=float, default=None, help="A rate, e.g. 0.1 for 10%%.")
    parser.add_argument("--thread", default=None, help="Thread to settle; defaults to the paused one.")
    parser.add_argument("--comment", default=None)
    args = parser.parse_args()

    secret = os.environ.get("WHATSAPP_APP_SECRET")
    if not secret:
        raise SystemExit(
            "WHATSAPP_APP_SECRET is required to sign the webhook. Read it with:\n"
            "  docker exec aveline_postgres psql -U aveline -d aveline -tAc \\\n"
            '    "select \\"EncryptedValue\\" from \\"IntegrationCredentials\\" where \\"IntegrationType\\"=\'WhatsApp\'"'
        )

    org_id = organization_id()
    print(f"organization : {org_id}")
    print(f"phone        : {args.phone}")

    if args.message:
        body = webhook_body(org_id, args.phone, args.message)
        status = send(args.api, org_id, body, secret)
        print(f"webhook      : HTTP {status}")
        if status >= 300:
            return 1

    print("paused run:")
    paused = show_pause(org_id, args.phone)

    if args.decide:
        target = args.thread or (paused or {}).get("thread")
        if not target:
            raise SystemExit("No thread to settle; pass --thread.")
        decision = "approved" if args.decide == "approve" else (
            "rejected" if args.decide == "reject" else "revised"
        )
        payload = {"thread_id": target, "decision": decision, "comment": args.comment}
        if args.revised_discount is not None:
            payload["revised_discount"] = args.revised_discount
        # Mirror what `ApprovalService.ResumePausedWorkflowAsync` sends. That payload is pinned by
        # `ApprovalResumeTests` on the .NET side; this script only reproduces it so the agent half can
        # be exercised against the running service, whose port is not published.
        if paused:
            payload["order_id"] = paused["order_id"]
            payload["customer_name"] = paused["customer_name"]
        print(f"\nresuming {target} as {decision} ...")
        result = resume(payload, internal_token())
        envelope = result.get("result") or {}
        commerce = (envelope.get("output") or {}).get("commerce") or {}
        print(f"  status       : {envelope.get('status')}")
        print(f"  commerce     : {commerce.get('status')}")
        print(f"  summary      : {commerce.get('summary')}")
        payment = commerce.get("payment") or {}
        if payment.get("url"):
            print(f"  payment      : {payment['url']}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
