#!/bin/sh
# Creates the least-privilege postgres_exporter role on first database initialisation.
#
# Docker runs this only when the data directory is empty. On an existing volume, run
# observability/postgres-exporter/role.sql by hand (the README documents the command).
set -e

: "${POSTGRES_EXPORTER_PASSWORD:?POSTGRES_EXPORTER_PASSWORD is required}"

psql -v ON_ERROR_STOP=1 \
     -v exporter_password="$POSTGRES_EXPORTER_PASSWORD" \
     -v database_name="$POSTGRES_DB" \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -f /docker-entrypoint-initdb.d/postgres-exporter-role.sql
