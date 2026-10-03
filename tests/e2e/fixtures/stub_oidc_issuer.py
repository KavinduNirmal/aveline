#!/usr/bin/env python3
"""A stub OpenID Connect issuer for the composed cross-surface E2E stack.

Why this exists
---------------
The API validates its bearer tokens for real with ``JwtBearer`` against
``Clerk:Authority`` (see ``Aveline.Api/Configurations/AuthenticationConfiguration.cs``).
The cross-surface walk in ``tests/e2e/integration/salon-cross-surface.spec.ts`` must
therefore present a token that the API genuinely validates: real signature, real
issuer, real lifetime, real JWKS discovery.  Reaching a hosted Clerk instance is not
possible without a human clicking through its sign-in page, and ``Aveline.Api.Tests``
already solves this same problem with ``StubAuthServer`` (a Kestrel server that serves
``/.well-known/openid-configuration`` and ``/.well-known/jwks.json`` from a generated
RSA key).

This fixture is that same pattern, without the ``dotnet test`` host around it: a small
standalone HTTP server that mints RS256 tokens and serves the two discovery documents.

**The identity provider is not the system under test.**  What remains real in the
composed stack is everything that matters: the API's own JWT validation pipeline, its
membership resolution against real PostgreSQL rows, its real agent HTTP hop, the real
Python agent service and the real Redis event round-trip that persists the answer.

No third-party packages
-----------------------
The agent service virtualenv does not ship ``cryptography`` or ``PyJWT``, so importing
one would make this fixture un-runnable in a plain CI Python.  RS256 signing is a
single ``pow(m, d, n)`` call, so the whole thing is implemented here on the standard
library: ``secrets`` for entropy, ``hashlib`` for SHA-256 and a Miller-Rabin test for
RSA key generation.  ``--selftest`` checks the resulting signature against
``cryptography`` when that package happens to be installed.

Endpoints
---------
``GET  /.well-known/openid-configuration``  discovery (``issuer`` + ``jwks_uri``)
``GET  /.well-known/jwks.json``             the public key, as JwtBearer fetches it
``POST /token``                             mint a token (JSON or form body: ``sub=...``)
``GET  /token?sub=...``                     the same, for a curl-driven seed script
``GET  /health``                            readiness, for the runner's wait loop

Usage
-----
    python3 tests/e2e/fixtures/stub_oidc_issuer.py --port 8899 --out /tmp/idp.json

``--out`` receives ``{"issuer", "port", "pid", "sub"}`` once the socket is bound, so a
runner can set ``Clerk__Authority`` to exactly the issuer this process serves.  The
server stays in the foreground; send SIGTERM (or POST ``/__shutdown``) to stop it.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import json
import os
import secrets
import signal
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

# --------------------------------------------------------------------------------------
# Minimal RS256 (RFC 7518 §3.3 / PKCS#1 v1.5) on the standard library.
# --------------------------------------------------------------------------------------

SMALL_PRIMES = [
    2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71,
    73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151,
    157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233,
    239, 241, 251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311, 313, 317,
    331, 337, 347, 349, 353, 359, 367, 373, 379, 383, 389, 397, 401, 409, 419,
    421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499, 503,
    509, 521, 523, 541, 547, 557, 563, 569, 571, 577, 587, 593, 599, 601, 607,
    613, 617, 619, 631, 641, 643, 647, 653, 659, 661, 673, 677, 683, 691, 701,
    709, 719, 727, 733, 739, 743, 751, 757, 761, 769, 773, 787, 797, 809, 811,
    821, 823, 827, 829, 839, 853, 857, 859, 863, 877, 881, 883, 887, 907, 911,
    919, 929, 937, 941, 947, 953, 967, 971, 977, 983, 991, 997,
]

#: Precomputed DigestInfo prefix for SHA-256 (RFC 8017 §9.2, note 1).
SHA256_DIGEST_INFO = bytes.fromhex("3031300d060960864801650304020105000420")


def _is_probable_prime(n: int, rounds: int = 32) -> bool:
    if n < 2:
        return False
    for p in SMALL_PRIMES:
        if n % p == 0:
            return n == p
    d, r = n - 1, 0
    while d % 2 == 0:
        d //= 2
        r += 1
    for _ in range(rounds):
        a = secrets.randbelow(n - 3) + 2
        x = pow(a, d, n)
        if x == 1 or x == n - 1:
            continue
        for _ in range(r - 1):
            x = x * x % n
            if x == n - 1:
                break
        else:
            return False
    return True


def _generate_prime(bits: int) -> int:
    while True:
        candidate = secrets.randbits(bits) | (1 << (bits - 1)) | 1
        if _is_probable_prime(candidate):
            return candidate


def generate_rsa_key(bits: int = 2048) -> dict[str, int]:
    """A fresh RSA private key as ``{"n", "e", "d", "p", "q"}`` ints."""
    e = 65537
    while True:
        p = _generate_prime(bits // 2)
        q = _generate_prime(bits // 2)
        if p == q:
            continue
        n = p * q
        if n.bit_length() != bits:
            continue
        phi = (p - 1) * (q - 1)
        try:
            d = pow(e, -1, phi)
        except ValueError:  # e shares a factor with phi; draw again.
            continue
        return {"n": n, "e": e, "d": d, "p": p, "q": q}


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def _int_to_b64url(value: int, length: int | None = None) -> str:
    if length is None:
        length = (value.bit_length() + 7) // 8 or 1
    return b64url(value.to_bytes(length, "big"))


def sign_rs256(key: dict[str, int], message: bytes) -> bytes:
    """PKCS#1 v1.5 SHA-256 signature, as JwtBearer's ``RsaSha256`` expects."""
    digest = hashlib.sha256(message).digest()
    k = (key["n"].bit_length() + 7) // 8
    t = SHA256_DIGEST_INFO + digest
    if len(t) + 11 > k:
        raise ValueError("RSA modulus too small for a SHA-256 signature.")
    em = b"\x00\x01" + b"\xff" * (k - len(t) - 3) + b"\x00" + t
    return pow(int.from_bytes(em, "big"), key["d"], key["n"]).to_bytes(k, "big")


def mint_token(
    key: dict[str, int],
    issuer: str,
    sub: str,
    ttl_seconds: int = 3600,
    kid: str = "stub-oidc-kid",
    extra_claims: dict | None = None,
) -> str:
    now = int(time.time())
    header = {"alg": "RS256", "typ": "JWT", "kid": kid}
    payload: dict = {
        "iss": issuer,
        "sub": sub,
        "iat": now,
        "nbf": now - 5,
        "exp": now + ttl_seconds,
        "jti": secrets.token_hex(8),
    }
    if extra_claims:
        payload.update(extra_claims)
    signing_input = f"{b64url(json.dumps(header, separators=(',', ':')).encode())}." \
                    f"{b64url(json.dumps(payload, separators=(',', ':')).encode())}"
    signature = sign_rs256(key, signing_input.encode("ascii"))
    return f"{signing_input}.{b64url(signature)}"


def jwks_for(key: dict[str, int], kid: str = "stub-oidc-kid") -> dict:
    return {
        "keys": [
            {
                "kty": "RSA",
                "use": "sig",
                "alg": "RS256",
                "kid": kid,
                "n": _int_to_b64url(key["n"]),
                "e": _int_to_b64url(key["e"]),
            }
        ]
    }


# --------------------------------------------------------------------------------------
# HTTP surface
# --------------------------------------------------------------------------------------

class IssuerState:
    def __init__(self, issuer: str, key: dict[str, int], default_sub: str, ttl: int) -> None:
        self.issuer = issuer
        self.key = key
        self.default_sub = default_sub
        self.ttl = ttl
        self.jwks = jwks_for(key)


def build_handler(state: IssuerState, stop: threading.Event):
    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"
        server_version = "aveline-stub-oidc/1.0"

        def log_message(self, fmt: str, *args) -> None:  # keep stdout readable
            sys.stderr.write(f"[stub-oidc] {self.address_string()} {fmt % args}\n")

        def _send(self, status: int, body: bytes, content_type: str = "application/json") -> None:
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def _json(self, status: int, payload: dict) -> None:
            self._send(status, json.dumps(payload).encode("utf-8"))

        def _sub_from(self, query: dict, body: dict) -> str:
            candidate = body.get("sub") or (query.get("sub") or [None])[0]
            return str(candidate).strip() if candidate else state.default_sub

        def do_GET(self) -> None:  # noqa: N802 - stdlib naming
            parsed = urlparse(self.path)
            query = parse_qs(parsed.query)

            if parsed.path == "/.well-known/openid-configuration":
                self._json(200, {
                    "issuer": state.issuer,
                    "jwks_uri": f"{state.issuer}/.well-known/jwks.json",
                    "token_endpoint": f"{state.issuer}/token",
                    "response_types_supported": ["id_token"],
                    "subject_types_supported": ["public"],
                    "id_token_signing_alg_values_supported": ["RS256"],
                })
                return

            if parsed.path == "/.well-known/jwks.json":
                self._json(200, state.jwks)
                return

            if parsed.path == "/health":
                self._json(200, {"status": "ok", "issuer": state.issuer})
                return

            if parsed.path == "/token":
                sub = self._sub_from(query, {})
                self._json(200, {
                    "access_token": mint_token(state.key, state.issuer, sub, state.ttl),
                    "token_type": "Bearer",
                    "expires_in": state.ttl,
                    "sub": sub,
                })
                return

            self._json(404, {"error": "not_found", "path": parsed.path})

        def do_POST(self) -> None:  # noqa: N802 - stdlib naming
            parsed = urlparse(self.path)
            length = int(self.headers.get("Content-Length") or 0)
            raw = self.rfile.read(length) if length else b""

            if parsed.path == "/__shutdown":
                self._json(200, {"status": "stopping"})
                stop.set()
                return

            if parsed.path == "/token":
                body: dict = {}
                if raw:
                    content_type = (self.headers.get("Content-Type") or "").split(";")[0].strip()
                    if content_type == "application/json":
                        try:
                            body = json.loads(raw.decode("utf-8")) or {}
                        except json.JSONDecodeError:
                            self._json(400, {"error": "invalid_json"})
                            return
                    else:
                        form = parse_qs(raw.decode("utf-8"))
                        body = {k: v[0] for k, v in form.items()}
                sub = self._sub_from({}, body)
                ttl = int(body.get("ttl") or state.ttl)
                self._json(200, {
                    "access_token": mint_token(state.key, state.issuer, sub, ttl),
                    "token_type": "Bearer",
                    "expires_in": ttl,
                    "sub": sub,
                })
                return

            self._json(404, {"error": "not_found", "path": parsed.path})

    return Handler


def selftest() -> int:
    """Cross-check the hand-rolled RS256 signature against ``cryptography`` if present."""
    try:
        from cryptography.hazmat.primitives import hashes
        from cryptography.hazmat.primitives.asymmetric import padding, rsa
    except ImportError:
        print("selftest: cryptography is not installed; signing self-check is skipped.")
        return 0

    key = generate_rsa_key(2048)
    message = b"aveline-stub-oidc-selftest"
    our_signature = sign_rs256(key, message)

    public = rsa.RSAPublicNumbers(key["e"], key["n"]).public_key()
    try:
        public.verify(our_signature, message, padding.PKCS1v15(), hashes.SHA256())
    except Exception as exc:  # noqa: BLE001 - the point is to report any mismatch
        print(f"selftest: FAILED - cryptography rejected our signature: {exc}")
        return 1

    print("selftest: OK - cryptography verified the hand-rolled RS256 signature.")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Stub OIDC issuer for the Aveline E2E stack.")
    parser.add_argument("--host", default="127.0.0.1", help="bind address (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8899, help="bind port; 0 picks a free one")
    parser.add_argument("--issuer-url", default=None,
                        help="issuer value to advertise; defaults to http://<host>:<actual port>")
    parser.add_argument("--sub", default="user_e2e_cross_surface",
                        help="default Clerk-shaped subject for minted tokens")
    parser.add_argument("--ttl", type=int, default=3600, help="token lifetime in seconds")
    parser.add_argument("--key-bits", type=int, default=2048)
    parser.add_argument("--out", default=None, help="JSON file receiving the bound issuer state")
    parser.add_argument("--selftest", action="store_true", help="verify RS256 against cryptography and exit")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()

    # Bind first so the advertised issuer carries the port the kernel actually gave us
    # (``--port 0`` is how a CI runner avoids a port clash).
    key = generate_rsa_key(args.key_bits)
    stop = threading.Event()
    server = ThreadingHTTPServer((args.host, args.port), BaseHTTPRequestHandler)
    actual_port = server.server_address[1]
    server.RequestHandlerClass = build_handler(
        IssuerState(
            issuer=args.issuer_url or f"http://{args.host}:{actual_port}",
            key=key,
            default_sub=args.sub,
            ttl=args.ttl,
        ),
        stop=stop,
    )

    state = {
        "issuer": args.issuer_url or f"http://{args.host}:{actual_port}",
        "host": args.host,
        "port": actual_port,
        "pid": os.getpid(),
        "sub": args.sub,
    }
    if args.out:
        tmp = f"{args.out}.tmp"
        with open(tmp, "w", encoding="utf-8") as handle:
            json.dump(state, handle)
        os.replace(tmp, args.out)

    print(json.dumps(state), flush=True)

    def _terminate(_signum, _frame) -> None:
        stop.set()

    signal.signal(signal.SIGTERM, _terminate)
    signal.signal(signal.SIGINT, _terminate)

    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        while not stop.is_set():
            time.sleep(0.2)
    finally:
        server.shutdown()
        server.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
