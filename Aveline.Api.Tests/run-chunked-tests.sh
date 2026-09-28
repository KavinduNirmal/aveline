#!/usr/bin/env bash
# Run the Aveline.Api test suite in filtered chunks.
#
# Why this exists: the suite constructs several hundred `WebApplicationFactory<Program>` hosts, and
# each one roots a `PhysicalFileProvider`, which spends an inotify instance. The host's
# `fs.inotify.max_user_instances` (1024 in this environment) is exhausted partway through a single
# `dotnet test` invocation, after which every remaining test fails with
#
#     System.IO.IOException : The configured user limit (1024) on the number of inotify instances
#     has been reached
#
# That is a host resource limit, not a test failure: the same tests pass when the run is split so
# each process starts with a fresh watch budget. ADR-024's W4.7 recorded the suite this way.
#
# The repo's `.env` is loaded for the app's media settings and the two values are given fallbacks:
# `MediaOptionsValidator` refuses to start a host that has `Media:Provider=cloudinary` and no signing
# key or public base URL, and a large block of integration tests builds such a host. The fallbacks
# only satisfy the validator; they are never used to reach a real provider.
#
# Usage: Aveline.Api.Tests/run-chunked-tests.sh [chunk-count]     (default 4)
#        EXCLUDE="Foo Bar" Aveline.Api.Tests/run-chunked-tests.sh  (skip classes by name)
set -uo pipefail

CHUNKS="${1:-4}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/.." && pwd)"
CLASSES="$(mktemp)"
trap 'rm -f "$CLASSES"' EXIT

# The app's media options are supplied through environment variables (`MEDIA_SIGNING_KEY`,
# `MEDIA_PUBLIC_BASE_URL`, `Media__Provider`, `CLOUDINARY_*`) and `dotnet test` does not read `.env`
# on its own. Without them `MediaOptionsValidator` fails fast in every host that boots with
# `Media:Provider=cloudinary`, which is most of the integration tests, so the suite reports hundreds
# of config failures that have nothing to do with the code under test. Loading the repo's own `.env`
# is what a developer running this suite already does by hand.
#
# An override passed on this script's command line wins over `.env`: the caller's values are captured
# first and restored after, so `Media__Provider=database run-chunked-tests.sh` means what it says.
CALLER_KEYS=()
for key in MEDIA_SIGNING_KEY MEDIA_PUBLIC_BASE_URL Media__Provider Media__SigningKey Media__PublicBaseUrl \
    Media__ReadFromCloudinary CLOUDINARY_URL CLOUDINARY_API_KEY CLOUDINARY_API_SECRET CLOUDINARY_CLOUD_NAME; do
    if [ -n "${!key:-}" ]; then
        CALLER_KEYS+=("$key=${!key}")
    fi
done

if [ -f "$REPO_ROOT/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    . "$REPO_ROOT/.env"
    set +a
fi

for entry in ${CALLER_KEYS+"${CALLER_KEYS[@]}"}; do
    export "${entry?}"
done

# Fallbacks so the suite is still meaningful on a checkout with no `.env`: these satisfy the
# validator and are never used to reach a real provider.
export MEDIA_SIGNING_KEY="${MEDIA_SIGNING_KEY:-AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=}"
export MEDIA_PUBLIC_BASE_URL="${MEDIA_PUBLIC_BASE_URL:-http://localhost:5091}"
export Media__SigningKey="${Media__SigningKey:-$MEDIA_SIGNING_KEY}"
export Media__PublicBaseUrl="${Media__PublicBaseUrl:-$MEDIA_PUBLIC_BASE_URL}"

# One class per line, sorted for a stable partition across runs. `EXCLUDE` is a space-separated list
# of class-name patterns to leave out (default: the live Cloudinary smoke tests, which sign against
# the real provider and cannot pass without its credentials).
EXCLUDE="${EXCLUDE:-CloudinaryLiveSmokeTests}"
grep -hoE '^public (sealed )?class ([A-Za-z0-9_]+)' "$HERE"/*.cs \
    | sed -E 's/^public (sealed )?class //' \
    | sort -u > "$CLASSES.tmp"
for pattern in $EXCLUDE; do
    grep -v -F "$pattern" "$CLASSES.tmp" > "$CLASSES.next" || true
    mv "$CLASSES.next" "$CLASSES.tmp"
done
mv "$CLASSES.tmp" "$CLASSES"

TOTAL="$(wc -l < "$CLASSES")"
PER=$(( (TOTAL + CHUNKS - 1) / CHUNKS ))
echo "running $TOTAL test classes in $CHUNKS chunks of <=$PER"

failed=0
for ((i = 0; i < CHUNKS; i++)); do
    mapfile -t names < <(sed -n "$((i * PER + 1)),$((i * PER + PER))p" "$CLASSES")
    [ "${#names[@]}" -eq 0 ] && continue

    filter=""
    for name in "${names[@]}"; do
        filter+="FullyQualifiedName~${name}|"
    done
    filter="${filter%|}"

    echo
    echo "=== chunk $((i + 1))/$CHUNKS (${#names[@]} classes) ==="
    dotnet test --no-build -c Debug --nologo \
        --filter "$filter" \
        --logger "console;verbosity=minimal" \
        || failed=1
done

exit "$failed"
