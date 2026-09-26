-- Merge a duplicate customer into the surviving record.
--
-- Why this exists: six tables hold a customer id, and a customer can be duplicated when identity
-- is derived from a phone number that later changes. Merging by hand is easy to get wrong, because
-- CustomerConsent is unique per (organization, customer) and CustomerTags is unique per
-- (customer, tag) - a blind UPDATE hits those constraints mid-migration.
--
-- Usage:
--   docker exec -i aveline_postgres psql -U aveline -d aveline \
--     -v dup=<duplicate-id> -v tgt=<surviving-id> < scripts/merge-customers.sql
--
-- Preview without changing anything by running the SELECTs alone, or wrap this file's body in a
-- transaction you roll back (it already runs as one):
--   ... -v dup=... -v tgt=... -c 'BEGIN;' -f - -c 'ROLLBACK;'
--
-- This is destructive and irreversible. Take a dump first if the records matter.

\set ON_ERROR_STOP on

BEGIN;

-- Guards, via a CHECK constraint rather than a division trick: a constant `1/0` can be folded at
-- plan time and fail even when the condition is false. A violated CHECK raises and, with
-- ON_ERROR_STOP, aborts the transaction before anything is written.
CREATE TEMP TABLE _merge_guard (ok boolean NOT NULL CHECK (ok)) ON COMMIT DROP;

-- Merging a record into itself would delete the survivor.
INSERT INTO _merge_guard VALUES (:'dup' <> :'tgt');

-- Both must exist, or the merge would silently do nothing.
INSERT INTO _merge_guard
VALUES ((SELECT count(*) FROM "Customers" WHERE "Id" IN (:'dup', :'tgt')) = 2);

\echo '--- before ---'
SELECT 'interactions' AS table_name,
       count(*) FILTER (WHERE "CustomerId" = :'dup') AS duplicate_rows,
       count(*) FILTER (WHERE "CustomerId" = :'tgt') AS target_rows
FROM "CustomerInteractions"
UNION ALL
SELECT 'memories', count(*) FILTER (WHERE "CustomerId" = :'dup'),
       count(*) FILTER (WHERE "CustomerId" = :'tgt')
FROM "CustomerMemory"
UNION ALL
SELECT 'events', count(*) FILTER (WHERE "CustomerId" = :'dup'),
       count(*) FILTER (WHERE "CustomerId" = :'tgt')
FROM "CustomerEvents"
UNION ALL
SELECT 'consents', count(*) FILTER (WHERE "CustomerId" = :'dup'),
       count(*) FILTER (WHERE "CustomerId" = :'tgt')
FROM "CustomerConsent";

-- Interactions, memories, events: no uniqueness beyond the primary key, so they move wholesale.
UPDATE "CustomerInteractions" SET "CustomerId" = :'tgt' WHERE "CustomerId" = :'dup';
UPDATE "CustomerMemory"       SET "CustomerId" = :'tgt' WHERE "CustomerId" = :'dup';
UPDATE "CustomerEvents"       SET "CustomerId" = :'tgt' WHERE "CustomerId" = :'dup';

-- Tags are unique per (customer, tag): drop the duplicate's copies of a tag the survivor has,
-- then move the rest.
DELETE FROM "CustomerTags" dup
 WHERE dup."CustomerId" = :'dup'
   AND EXISTS (
         SELECT 1 FROM "CustomerTags" tgt
          WHERE tgt."CustomerId" = :'tgt' AND tgt."Tag" = dup."Tag"
       );
UPDATE "CustomerTags" SET "CustomerId" = :'tgt' WHERE "CustomerId" = :'dup';

-- Preferences have no uniqueness beyond the primary key.
UPDATE "CustomerPreferences" SET "CustomerId" = :'tgt' WHERE "CustomerId" = :'dup';

-- Consent is unique per (organization, customer), so the two cannot coexist. The survivor's row is
-- kept, EXCEPT when the duplicate had revoked: a revocation must never be discarded by a merge, so
-- in that case the survivor is revoked too. Erring the other way would silently resume processing a
-- customer who opted out.
UPDATE "CustomerConsent" tgt
   SET "ConsentStatus" = 'revoked', "UpdatedAt" = now()
 WHERE tgt."CustomerId" = :'tgt'
   AND EXISTS (
         SELECT 1 FROM "CustomerConsent" dup
          WHERE dup."CustomerId" = :'dup' AND dup."ConsentStatus" = 'revoked'
       );

DELETE FROM "CustomerConsent" WHERE "CustomerId" = :'dup';
DELETE FROM "Customers"        WHERE "Id" = :'dup';

\echo '--- after ---'
SELECT 'interactions' AS table_name, count(*) AS target_rows
FROM "CustomerInteractions" WHERE "CustomerId" = :'tgt'
UNION ALL SELECT 'memories', count(*) FROM "CustomerMemory" WHERE "CustomerId" = :'tgt'
UNION ALL SELECT 'events', count(*) FROM "CustomerEvents" WHERE "CustomerId" = :'tgt'
UNION ALL SELECT 'consents', count(*) FROM "CustomerConsent" WHERE "CustomerId" = :'tgt';

\echo '--- survivors with that name or number ---'
SELECT "Id", coalesce("FullName", '(no name)') AS full_name, "PhoneNumber"
  FROM "Customers"
 WHERE "PhoneNumber" LIKE '%94763475058%' OR "PhoneNumber" LIKE '%94771234567%';

COMMIT;
