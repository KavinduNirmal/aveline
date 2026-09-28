-- Least-privilege role for postgres_exporter (Slice 6, S-10/R-22).
--
-- pg_monitor is a predefined role that transitively grants pg_read_all_settings,
-- pg_read_all_stats and pg_stat_scan_tables. That is exactly what the five collectors this
-- deployment uses require (pg_locks, pg_stat_database, pg_database, pg_stat_activity and
-- pg_stat_replication), and it grants NO access to application tables. No superuser.
--
-- Run with:
--   psql -v ON_ERROR_STOP=1 -v exporter_password='<secret>' -v database_name='aveline' -f role.sql
--
-- NOTE: psql does NOT substitute :'variables' inside a dollar-quoted DO block, so the
-- create/alter statements are built with format() and executed with \gexec instead. That keeps
-- the password out of the statement log and keeps the script idempotent.

\set ON_ERROR_STOP on

-- Create the role if it is absent...
SELECT format('CREATE ROLE postgres_exporter LOGIN PASSWORD %L', :'exporter_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'postgres_exporter')
\gexec

-- ...and rotate its password if it already exists, so a secret change is applied by re-running.
SELECT format('ALTER ROLE postgres_exporter WITH LOGIN PASSWORD %L', :'exporter_password')
WHERE EXISTS (SELECT FROM pg_roles WHERE rolname = 'postgres_exporter')
\gexec

-- Monitoring views only. Deliberately no GRANT on any application table or schema.
GRANT pg_monitor TO postgres_exporter;

-- psql's :"..." form quotes the value as an identifier.
GRANT CONNECT ON DATABASE :"database_name" TO postgres_exporter;
