# Phase 1 migration and rollback runbook

Budget revision, 8 September 2026: the user deferred additional Railway spending. This runbook is retained for a future PostgreSQL cutover; do not execute its provisioning/deployment steps now. Production stays on the existing JSON service and volume. Phase 1 implementation/local rehearsal is complete for the budget track, and independent feature work can continue under FIRST_PUBLIC_VERSION_PLAN.md. PostgreSQL hosting and a separate backup worker are not current development prerequisites. Existing JSON snapshots plus operator-verified off-volume copies remain the current recovery path; automated off-volume transfer is not yet implemented.

The contract below was recorded before implementation. Commands and local evidence follow it. Production execution is a separate recorded gate; local tests do not satisfy that gate.

JSON remains the default provider. PostgreSQL is enabled only by explicit configuration after import and comparison. No live volume or production database is modified by implementation/testing.

The modular projects separate pure domain data/rules, application operations and persistence ports, infrastructure adapters, and HTTP composition/endpoints. The root executable remains a compatibility entry point for existing deployment commands.

Migration creates a normalized relational schema with UUID primary/foreign keys. Legacy event/day/performance/group/device identifiers remain unique public aliases. Existing performance IDs, picks and links do not change. Accounts gain internal UUIDs; private-beta display-name API contracts remain until Phase 2. Stable import mappings are deterministic and stored in the relational rows. No email, owner, IANA timezone or other missing legacy metadata is guessed. UTC instants retain original offset metadata for unchanged rendering; event timezone modelling is a later explicit migration.

Import accepts only characterized JSON versions 5–7. It validates required collections, counts, duplicate keys and references before connecting or writing; older destructive legacy migrations are not invoked. Dry-run is offline. Import is one transaction into an empty schema, records the source digest, refuses populated destinations and rolls back on any count/hash mismatch. A repeated identical source can be recognized and verified without overwriting live edits. Comparison reads both sides and outputs only counts and canonical hashes, never private row contents or connection strings.

For this compatibility phase, an operation acquires a PostgreSQL transaction advisory lock before loading state, then persists normalized tracked changes atomically. This conservative serialization prevents lost updates across processes while business behaviour is extracted. It is not the final query/performance architecture; granular repositories should replace whole-catalogue reads in subsequent feature slices. Do not increase reminder-worker replica count until the durable job phase.

Cutover: capture named volume/key backup; retain immutable source; dry-run; stop writes/reminder worker; import; compare every collection plus canonical content hash; deploy configured PostgreSQL app; verify core journeys; reopen traffic. No automatic database migrations or automatic seeding occur on PostgreSQL startup. A missing schema/import must fail closed.

Rollback before writes reopen: stop PostgreSQL app, restore previous image/configuration and the untouched JSON snapshot. After PostgreSQL accepts new writes, switching back to the old JSON loses those writes: require a maintenance window, validated reverse export or explicit loss decision. Never destructively down-migrate production. Retain source/key backups for at least two festival cycles.

Daily backups use pg_dump custom format and atomic finalization; restore rehearsal targets a newly created isolated database, verifies migrations/counts/canonical hashes and cleans up only that database. Production backup jobs must run with protected credentials and a mounted destination separate from app source. Off-volume retention and staging/production deployment verification remain operator gates.

## Build and configuration

Production-copy preflight on 8 September found sub-microsecond timestamp precision in legacy JSON. Additive migration `20260908064441_PreserveLegacyTimestampPrecision` stores each legacy timestamp's final 100-nanosecond digit in a constrained remainder column, preserving exact values alongside PostgreSQL timestamps. Existing rows default to zero; no source mutation or canonical-hash weakening is permitted. Revert only in isolated tests; removing these columns after import would lose precision.

```text
dotnet restore RiffRoster.sln
dotnet publish RiffRoster.csproj -c Release -o work/publish
dotnet publish tools/RiffRoster.DataMigration -c Release -o work/migration
```

Use `dotnet work/migration/RiffRoster.DataMigration.dll` for the commands below. In the web Docker image the tool is `/app/migration/RiffRoster.DataMigration.dll`.

| Setting | Meaning |
| --- | --- |
| `RIFFROSTER_STORAGE` | `Json` (default) or `Postgres`; unknown values fail startup. |
| `RIFFROSTER_POSTGRES_CONNECTION` | Npgsql connection string supplied through the platform secret environment. Required for PostgreSQL and online migration commands. Never put it in command arguments or reports. |
| `RIFFROSTER_READ_ONLY` | `true` rejects API mutations with 503 and stops the reminder worker. Login also counts as a mutation. Existing authenticated reads and health remain available. |
| `RIFFROSTER_PG_BIN` | Directory containing PostgreSQL `pg_dump` and `pg_restore`; optional if on PATH. Use client version matching the database major version (tested: 17). |
| `RIFFROSTER_KEYS_PATH` | Preserve the existing session key directory across cutover and rollback. |

The runtime database role needs data read/write permissions; migration privileges and restore-drill `CREATE DATABASE` privileges belong to an operations role. Use verified TLS when connecting across networks. Provision database credentials through Railway rather than storing them in the repository.

## Production preflight and cutover

1. Record the project, environment, service, current deployment image/commit, volume mount, database target and operator. The user selected Production; therefore use an isolated database for rehearsals and never replace the active database to test restore.
2. Take and verify a named platform volume backup containing JSON and session keys. Keep an immutable source copy outside the live path. Record its checksum privately. Do not copy production rows or credentials into this repository.
3. Run `dry-run SOURCE.json`. It performs no database access. Resolve any validation failure; do not delete rows to force an import. Counts, source digest, canonical digest and orphan count are the review evidence. Version 5/6 defaults are normalized to version 7; array order is ignored by the canonical comparison while display positions are retained in relational rows.
4. Point the operations process to an empty rehearsal database. Run `migrate`, `import SOURCE.json`, then `compare SOURCE.json`. Record `Matches: true`, every count, and canonical digest. Run `backup BACKUP-DIRECTORY` and `restore-drill DUMP` there. The latter creates and removes its own random database on the connected server; it needs database-creation privileges. Prefer a separate rehearsal server so load and permissions cannot affect production.
5. Rehearse rollback using the export procedure below and an isolated application instance. Disable actual push delivery. Use synthetic users for destructive browser journeys; never run the automated test suite against Production.
6. Deploy the compatible JSON build with `RIFFROSTER_READ_ONLY=true`, wait for the prior writable instance/reminder worker to stop, and take the final immutable JSON/key snapshot. Repeat dry-run on this final source; an earlier snapshot may be stale.
7. Set the operations connection to the intended empty production PostgreSQL database, run `migrate`, `import FINAL-SOURCE.json`, and `compare FINAL-SOURCE.json`. Require both `20260907193339_InitialRelationalStore` and `20260908064441_PreserveLegacyTimestampPrecision`. Import is transactional and does not overwrite a populated database. The application does not run migrations itself.
8. Deploy with `RIFFROSTER_STORAGE=Postgres`, the correct connection, unchanged access/VAPID codes and keys, and read-only still enabled. Verify health and existing authenticated schedule/group reads. Record deployment and comparison evidence before enabling writes.
9. Set read-only to false, smoke-test login, an agreed disposable pick and group, schedule editing with stable IDs, and restart persistence. Keep one reminder worker. Observe errors and confirm the backup job before declaring cutover complete.
10. Retain immutable JSON and keys for at least two festival cycles, along with a current database backup. Record the restore location, retention policy and operator responsible for recovery.

## Daily PostgreSQL backups

The web process runs JSON snapshots only when using JSON. PostgreSQL needs a separately supervised operations process:

```text
dotnet work/migration/RiffRoster.DataMigration.dll backup /protected/backups
dotnet work/migration/RiffRoster.DataMigration.dll backup-loop /protected/backups
dotnet work/migration/RiffRoster.DataMigration.dll restore-drill /protected/backups/riffroster-YYYYMMDD.dump
```

`backup-loop` checks every six hours and creates at most one archive per UTC day. A failure exits nonzero for the supervisor to alert and restart. Configure an alert for a missing/stale backup, plus off-volume encrypted replication and retention. The tool does not prune PostgreSQL backups. Preserve the `.dump` and matching `.dump.json` manifest together; neither contains a connection string, but the archive contains private application data.

Archive contents, counts and migration history are captured from the same exported database snapshot. Restore verifies file digest, every canonical count/hash, migration history and import receipt. A missing/corrupt manifest fails closed. If interruption leaves an incomplete archive/manifest pair, investigate and quarantine it before retrying; the tool will not overwrite it.

An optional worker image includes PostgreSQL 17 clients:

```text
docker build --target operations -t riffroster-operations .
```

Run that image with protected connection environment, a persistent backup mount, and arguments `backup-loop /backups`. Its default entrypoint is the migration tool; the default Docker build remains the web image. Docker image execution and remote CI remain unverified locally because Docker is unavailable.

## Rollback with no lost writes

Before any PostgreSQL writes, stop the new instance and restore the previous deployment/configuration pointing to a working copy of the untouched final JSON snapshot and original keys.

After PostgreSQL writes, freeze mutations/reminders and wait for previous writers to stop. Take a database backup first, then:

```text
dotnet work/migration/RiffRoster.DataMigration.dll report
dotnet work/migration/RiffRoster.DataMigration.dll export NEW-ROLLBACK.json
dotnet work/migration/RiffRoster.DataMigration.dll dry-run NEW-ROLLBACK.json
dotnet work/migration/RiffRoster.DataMigration.dll compare NEW-ROLLBACK.json
```

Export refuses an existing path. Require matching counts and canonical digest, then boot the JSON provider against a separate working copy in isolation and verify reads before selecting it for production rollback. Preserve the export, backup and session keys. Do not down-migrate production or point the old application at a stale JSON file after accepting new writes. A rollback to an earlier binary also requires its supported JSON schema to remain version 7.

## Local evidence and remaining gates

On 7–8 September 2026, real ephemeral PostgreSQL 17.11 databases passed import/count/hash/stable-ID checks for v5/v7 fixtures, forced transaction rollback, referential constraints, concurrent writes, API regression, archive restore/corruption checks, exact timestamp precision, and PostgreSQL-to-JSON reopening after new writes. The full .NET suite has 51 passing tests; existing Node (6), Python (3), and Chromium journeys on each provider (5 + 5) also passed. PostgreSQL browser journeys were rerun after the precision migration. Release app and migration builds, formatting and model/migration synchronization passed. Automated tests use synthetic data only.

Separately, the selected Production source was copied and verified off-volume on 8 September. Railway platform backups were unavailable on this account's plan. A named JSON/key archive was created using a private new backup directory, leaving live files intact; this is an application backup, not a platform volume snapshot. Its version 5 source passed offline validation, transactional import into an isolated local database, complete count/hash comparison, PostgreSQL dump/restore, reverse export comparison, and startup of the JSON provider in read-only mode with push disabled. The first import rolled back because of timestamp precision; the additive migration fixed the issue and the unchanged source then passed. Private archives remain outside the repository. This was a local production-copy rehearsal, not a Railway staging deployment or production cutover.

Production project identification and authentication do not constitute deployment validation. Named production backup, final source comparison, deployed smoke tests, off-volume backup supervision and environment rollback rehearsal must be recorded when actually executed. Phase 1 remains awaiting these external exit gates.
