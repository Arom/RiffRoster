# First public version implementation log

## Phase 0 — Baseline and safety net (7 September 2026)

Scope: characterize the current deployable app before changing its architecture. Follow the plan's one-phase-at-a-time rule. This phase does not implement or claim a public launch.

- [x] Characterize API/auth, seed schedule, groups, stable edits/deletion and reminders.
- [x] Load synthetic legacy and current JSON fixtures; verify backup/restore.
- [x] Exercise browser login, event switching, picks, groups, admin editing/deletion, poster review and mocked notifications.
- [x] Record mobile Lighthouse and screenshots at all three requested sizes.
- [x] Record architecture and migration/rollback decisions.
- [x] Run build, formatting verification and relevant tests; record limitations.

Data impact: no new schema or production migration. Test hosts use unique temporary directories. Browser runs must start their own isolated app. Never point this suite at a deployed service. Fixture names, credentials and endpoints are synthetic. No production data is copied into source control.

Rollback: revert Phase 0 tracked changes; runtime data remains unchanged. Before Phase 1, take an immutable store and key backup, verify it in isolation and rehearse restore. The existing loader can change old schema versions, so never use the only backup as the application's live input.

Known baseline risks to carry forward: malformed JSON currently causes preserved-invalid-file plus reseeding; pre-v2 migration clears users/picks; names are foreign keys; JSON is single-replica; auth is private-beta only; fixed-offset/browser-local time handling lacks event timezone support; OCR error objects are logged; crew-name reminders default on; no durable offline selection outbox. These are characterization findings, not approved public-version behaviour.

### Verified result

Phase 0 local implementation complete. 22 xUnit characterization tests, 6 Node frontend/service-worker tests, 3 Python backup-tool tests and 5 Chromium journeys passed (36 total; no skipped tests). .NET solution build, release publish, scoped C# whitespace verification and git whitespace checks pass. Browser tests required Windows process-control permission so the runner could shut down its owned server tree. Dependency installation needed network access; no credentials were added to source.

Lighthouse 12.8.2 / Chromium 145.0.7632.6: Performance 100, Accessibility 96, Best Practices 96, SEO 90. This is one local mobile-throttled authenticated run, not representative production load validation. Full reports, score metadata and three screenshots are under `docs/baseline/`.

The release publish inspection found pre-existing local App_Data fixture files would be included by the default SDK glob. Packaging now excludes App_Data, test tools/dependencies/results and baseline artifacts; a fresh release publish was inspected and contains none of those files. Original App_Data files were not modified. Runtime endpoint, UI and persistence behaviour are unchanged.

Staging was not deployed or tested. The CI workflow has been written and its constituent commands run locally, but no remote Actions run is claimed. Physical-device installation, real push transport, real OCR accuracy, production restore and manual screen-reader/200% zoom certification remain outstanding. They are not implied by passing mocks or Lighthouse.

### Next phase boundary

Phase 0 finished before Phase 1 started. Remaining phases and external pilot/launch gates are not complete.

## Phase 1 — Modular backend and PostgreSQL (7 September 2026)

- [x] Extract Domain, Application, Infrastructure and API projects; retain HTTP behaviour.
- [x] Add normalized EF Core schema, checked-in migration and explicit provider switch.
- [x] Add validated dry-run/import/compare tooling, stable mappings and all-or-nothing import.
- [x] Test actual PostgreSQL transactions, constraints, restart and cross-instance writes.
- [x] Add daily pg_dump backup and isolated restore rehearsal tooling; verify locally.
- [x] Run baseline regression/browser/build/architecture checks and document local evidence.
- [ ] Record named production backup, final source comparison and deployment smoke results.
- [ ] Rehearse rollback against an isolated copy of the selected environment and configure supervised off-volume backups.

Migration and rollback contract written before storage edits: PHASE_1_MIGRATION.md, now expanded with executable operational instructions. No live source data changes. Docker is unavailable locally; isolated portable PostgreSQL 17.11 provides real local verification, with Testcontainers configured for CI.

Local result: 51 .NET tests passed (22 characterization, 6 domain, 8 application/import validation, 2 architecture, 13 PostgreSQL integration). Existing Node 6 and Python 3 tests passed, along with all 5 Chromium journeys on JSON and all 5 on PostgreSQL. These include stable legacy IDs, atomic rollback on forced import failure, cross-instance pick concurrency, migration/constraint checks, daily dump/isolated restore/corruption rejection, exact timestamp ticks, and reverse export reopening in JSON after PostgreSQL writes. No production data is used by automated tests.

Provider selection remains opt-in. The PostgreSQL runtime fails closed without a migrated/imported schema; the importer refuses populated or changed destinations. Production must retain session keys, access codes and VAPID keys. This extraction deliberately keeps the current private-beta account model and coarse whole-state PostgreSQL operation lock; public identity and scalable queries remain later work. The legacy JSON malformed-input fallback is still characterized, not approved for public launch.

The user selected the Railway Production environment for operational validation. External backup/comparison/cutover gates remain open until performed and recorded. Local tests and written CI/Docker configuration do not imply a remote deployment or restore. Phases 2–8 are not implemented by this phase.

### Production-copy rehearsal — 8 September 2026

Read-only discovery confirmed one active RiffRoster replica with a `/data` volume and no PostgreSQL service. Platform snapshots/PITR require a higher Railway plan. Created a named private JSON/key archive, verified its SHA-256 after download, and retained the source outside the repository. Live deployment and runtime files were not replaced; production health remained OK.

The unchanged version 5 source contains 2 events, 9 days, 217 performances, 6 users, 199 selections and 3 subscriptions (no groups, memberships or deliveries). Validation found zero orphans. Initial import correctly rolled back on a canonical mismatch caused by sub-microsecond timestamps. Added and tested `PreserveLegacyTimestampPrecision`; a retry then matched all counts and the full canonical hash. Dump/isolated restore, reverse export and read-only JSON startup on that copy also passed with push disabled.

Original remaining cutover work: provision production PostgreSQL/backup services, establish automated off-volume retention, build/deploy remotely, take a fresh maintenance-window snapshot, and run final production comparison/smoke checks. This work is now deferred by the budget revision below. The rehearsal backup is not a substitute for a final cutover snapshot. No account-plan upgrade, new paid service, git push or production deployment has occurred.

### Budget revision — 8 September 2026

The user directed continued implementation without additional Railway budget and requested notification only when progress is demonstrably impossible without an increase. Phase 1 implementation and local production-copy rehearsal are complete for this track. Hosted PostgreSQL cutover, separate backup workers and hosted staging are deferred; unchecked deployment items above are not implementation blockers or completed deployment claims.

Next slice: Phase 1B JSON reliability (fail closed on corrupt/unsupported input, preserve source, verify atomic saves and recovery). Then advance the compatible TypeScript shell/timeline slice and independent groups/import/notification work. Public Identity and billing may be developed locally but remain gated in production until persistence, email delivery, authorization and recovery are validated. Do not remove the current private access gate as a budget workaround.

Keep one existing Railway service/replica/volume and existing daily JSON snapshots. Verified off-volume copies currently require an operator; automated transfer remains unimplemented. No new paid provider, resource increase or plan upgrade is authorized. Escalate only a concrete blocked requirement after evaluating no-additional-cost alternatives, with evidence and the smallest costed proposal. No runtime, schema or production changes were made by this planning revision.

## Phase 1B — JSON reliability

Contract before implementation: retain schema version 7 and support validated v5–7 input only. Preserve an immutable pre-upgrade copy before normalizing older supported input. Corrupt, incomplete, unsupported or orphaned input must stop startup with the original bytes intact; never use seed data as recovery. New installations with no source file retain explicit existing bootstrap behavior. Serialize changes to a separate file, flush before replacement, and publish in-memory state only after the disk replacement succeeds. Single-process sessions must serialize access and discard failed/unsaved mutations. Keep one production replica; this does not add cross-process JSON concurrency support.

Rollback: no new data schema. Retain the latest valid JSON and keys; restore a verified working copy under maintenance. Reverting the binary alone would reintroduce unsafe fallback behavior. This slice is local until deployment smoke/backup checks are performed; no Railway change is required for implementation.

Implemented and verified locally: strict startup validation, preserved pre-upgrade source, flushed temporary writes with atomic replacement, isolated sessions that commit memory after persistence, and validation of daily snapshots. The original malformed-input regression now requires refusal instead of reseeding. Seven additional cases cover unsupported/null/incomplete input, legacy backup preservation, replacement failure/retry/restart, discarded unsaved state and corrupt daily backup reporting.

Validation: all 58 .NET tests passed (29 characterization/reliability, 6 domain, 8 application, 2 architecture, 13 PostgreSQL integration), all 5 JSON Chromium journeys passed, release publish and whitespace verification passed. A first PostgreSQL run targeted the former port after a local restart; correcting the disposable server connection resolved those connectivity failures. No Railway resources, production files or production deployment changed. No new schema migration. JSON is still one process; power-loss and filesystem guarantees beyond the tested flush/replace behavior are not claimed.

Phase 1B implementation complete. Next independent slice: Phase 3 compatible TypeScript shell/timeline foundation. Production deployment remains separately subject to backup and smoke checks on the existing service; no budget increase is needed to continue development.

## Phase 3A — TypeScript build, day navigation and timeline geometry

Scope before implementation: establish strict TypeScript and Vite, mount React only inside the existing day-navigation container, and extract Schedule/My route geometry into shared typed functions. Preserve the current API, login, groups, imports, styling and selections. Add roving keyboard navigation to day tabs. Package assets in the same application image and precache the new bundle. No persisted schema or production migration. Public identity, a complete React rewrite, IndexedDB/outbox, generated API client and launch accessibility certification remain later slices. Rollback restores the prior frontend assets/service worker together; data is unchanged.

Implemented: `src/RiffRoster.Web` contains strict TypeScript geometry and a React day-navigation component. The legacy controller calls an explicit compatibility bridge; no duplicate geometry implementation remains. Arrow keys/Home/End activate and focus days, the crew selector stays synchronized, and mobile tabs keep readable widths with horizontal scrolling. The panel is labelled for assistive technology. Existing server/auth/group/import/pick contracts remain intact.

Build/persistence impact: Vite emits an ignored generated bundle; a .NET build checks that assets exist, and Docker/CI build them first. The final container remains the same ASP.NET Core service. A content-derived service-worker cache version includes the new precached bundle and shell logic. Offline shell assets work; authenticated API data is not cached. No data migration, source account changes, production deployment or new paid service.

Validation: strict typecheck, 8 Node frontend checks, 7 Chromium journeys and 29 .NET characterization/reliability tests passed. The first restricted browser run required cleanup of its owned localhost server; subsequent runs with process-control access completed normally. Release publish, source/published frontend hash comparison, C# whitespace verification and git whitespace checks passed. No tests/tools/node_modules/App_Data leaked into the release. Docker execution and remote CI remain unverified.

Visual review at mobile and desktop widths caught and fixed cramped day labels. Fresh Lighthouse 13.4.1 evidence under ignored `work/phase3/visual-checks` records Performance 100, Accessibility 96, Best Practices 100, SEO 90 on one synthetic local authenticated run. Original Phase 0 measurements were retained. The new React/TypeScript bundle is 191.89 kB raw / 60.70 kB gzip (not total application size). Full device, screen-reader, load and offline schedule/outbox certification remain outstanding.

Dependency audit surfaced two existing Lighthouse transitive advisories. Updating the development-only Lighthouse package to 13.4.1 resolved both; `pnpm audit` reports no known vulnerabilities. Fresh Lighthouse execution verified the updated tool. Phase 3A is complete locally; Phase 3B schedule components and typed state/API boundaries are next. The broader Phase 3 is not complete.

## Phase 3B — React schedule and typed request transport

Scope: migrate the complete schedule grid, empty states and cards to React, retaining delegated pick/edit/attendee actions and existing geometry. Preserve stable card nodes across pick updates. Close legacy-owned attendee popovers before each React render. Move JSON request transport into strict TypeScript, retain status-specific errors and reject invalid successful JSON. Endpoint payloads remain unknown at this incremental boundary; runtime state decoders and generated contracts remain later work. No persisted schema, API route or infrastructure change. Rollback restores frontend assets and service worker together.

Implemented the schedule component and request transport. React owns the full schedule container, including empty transitions; stable performance keys retain focused pick buttons after updates. Legacy attendee popovers close before rendering to keep DOM state synchronized during the transition. API failures safely handle null/object message bodies; successful malformed JSON is rejected and 204 is supported. Runtime endpoint decoders remain pending.

Validation: strict TypeScript build, 11 Node checks, 8 Chromium journeys and .NET build passed. New coverage checks request headers/body, authentication/error statuses, malformed JSON/no-content responses, retained card focus, attendee expansion and empty-to-populated transitions. Reviewed mobile and desktop screenshots; git whitespace check passed. Bundle 196.96 kB raw / 62.38 kB gzip. No backend/schema change, production deployment or Railway spend. Full .NET suites and Lighthouse were not rerun for this frontend slice. Next: typed state payload validation and models.

## Phase 3C — Validated state snapshots

Scope: derive TypeScript public snapshot models from existing server projections and validate both state-loading paths before client state replacement. Check fields, dates, identities and selection/group references; copy only public fields. Preserve overnight acts, seven-digit timestamps and stale preferred-event fallback. Endpoint mutation responses and full controller typing remain later work. No API or persisted schema change, Railway change or deployment. Rollback restores frontend assets/service worker together.

Implemented `StateSnapshot` and nested public models with a decoder that returns copied, known fields. Both state fetch sites validate before assignment. Invalid payloads produce a fixed error without echoing values. Hex colors are checked before legacy style interpolation. Selection identities/references and visible group members are checked. Stale preferred-event IDs remain tolerated by the existing fallback.

Validation: 14 Node tests and 9 Chromium journeys passed, strict TypeScript and .NET build passed, and git whitespace verification passed. New browser coverage verifies an invalid snapshot stays out of the UI and valid login recovers. Existing crew, admin, OCR, notification and pick workflows pass against actual server snapshots. Bundle 199.44 kB raw / 63.24 kB gzip. Full backend suites, Lighthouse and deployment were not rerun for this frontend-only change. No database schema, server contract or Railway resources changed. Next slice: typed mutation endpoint responses; generated contracts and the complete controller migration remain outstanding.

## Phase 3D — Validated schedule and group mutations

Scope: typed response decoders and endpoint methods for picks, event/act creation and deletion, edits, bulk imports, groups and preferred event. Check returned identifiers before local updates, reject malformed results and advise refresh because server writes may have committed. Never automatically retry a mutation. Session and push response validation, generated contracts and remaining controller typing stay open. No server, schema or Railway change; rollback restores the frontend and matching service worker.

Implemented typed mutation endpoint methods and public result decoders. Eleven legacy call sites now validate results before changing local state. Existing record decoders are shared for events, performances and users. IDs are checked against requests for edits/deletes, groups, selection toggles and preferred events; bulk IDs are unique and counters are nonnegative safe integers. Invalid successful JSON and invalid response shapes produce a fixed refresh-before-retry message. Transport status errors remain unchanged; no automatic retry is introduced.

Validation: strict typecheck, 17 Node tests, 10 Chromium journeys, .NET build and git whitespace checks passed. The new browser case verifies a mismatched pick response leaves the route unchanged and triggers one request only. Existing real-server admin/group/import flows passed. Bundle 202.26 kB raw / 64.00 kB gzip. Backend suites, Lighthouse and production deployment were not rerun for this frontend-only slice. No schema or Railway change. Session/push responses, typed request construction and generated API contracts remain outstanding.


## Phase 3E — Session/push validation and release candidate

Completed typed validation for login, notification configuration, subscription saves and send acknowledgement. Logout and push removal require HTTP 204; malformed successful results do not trigger automatic retries. All remaining legacy API calls now use endpoint methods. The notification browser fixture now matches server response shapes. No server or schema changes.

Validation: 20 Node checks and 10 Chromium journeys passed, along with strict typecheck and .NET build. Release publish to ignored work/phase3/release-candidate passed; bundle, controller, worker and index hashes match source, and data/development directories are excluded. Whitespace check passed after removing a trailing blank line. Bundle 203.68 kB raw / 64.43 kB gzip. Full backend regression, remote image build, live backup/rollback checks and actual device push delivery remain release work; browser mocks are not delivery proof.

Deployment sequencing is now explicit in the plan: the next milestone is a release of the completed work on the existing Railway JSON service, before further feature migration. A fresh verified off-volume backup, reproducible source revision, existing volume/configuration verification and rollback target are still required. No deployment date or successful deployment is claimed; no additional budget required by this milestone.


## Release preflight — 9 September

Release regression: all 58 backend tests passed after configuring the disposable PostgreSQL server and backup utility path. The initial sandbox server-start failure caused connection failures; the following run lacked the pg_dump path. Both setup issues were corrected, all 13 PostgreSQL integration tests passed, and the disposable server was stopped. The latest frontend verification remains 20 Node checks and 10 Chromium journeys, with successful release publish.

Production inspection confirmed the existing online service, one replica in US West, Dockerfile build, /api/health health check and automatic deployment from Arom/RiffRoster main. Wait for CI is currently off. No settings were changed. Active deployment: cf412b54-0050-4aad-b62b-4b5ae03b9ecc, Fix Web Push reminder headers. Source configuration references c2ed125d8d93a3dc4924a12e0eb8a62ad3d61166. Local base HEAD is 374067942494b25fd1dece4fabfbda155631b417; the implementation is still uncommitted. Do not push main until release checks and backup/rollback preparation are complete.

Confirmed /data/store.json, /data/keys and /data/backups. Created a separate unique release-preflight-20260909 directory containing a copied store and keys plus store-and-keys.tar.gz. The copied store matched the live source immediately after copying. The console reported an archive checksum; off-volume download and independent checksum verification remain outstanding. The live store and keys were not modified. This is a preflight copy taken while the service remained writable, not a final maintenance-window snapshot. Download and structural verification remain outstanding.

A local source candidate with 136 files and a per-file SHA256 manifest was created under ignored work/release. Source archive SHA256: 8145c79882b958726cba875a3d4c8ee520e2b05167f331995f5d43f117fdfe01. It excludes ignored production data, private backup files and build directories. It is not a Git commit or deployed release.

Remaining: download/verify the preflight backup, complete fresh cutover backup and rollback rehearsal, review and commit the source, build the remote image, then deploy and smoke-test the existing service. No paid resources, upgrade or production deployment occurred.


## Backup download handoff — 9 September

Confirmed the archive is visible at /data/backups/release-preflight-20260909-hwdd7u/store-and-keys.tar.gz (9.1 KB) in the authenticated Railway file browser. Two Download actions did not produce a local file, including one explicitly tracked attempt that timed out after 20 seconds. No independent checksum/restore verification is claimed. Requested the user download this archive manually and provide its local path. Do not paste keys or archive content into the conversation. The browser is left on the backup folder.

Additional release checks: all three Python backup-tool tests passed and C# whitespace verification passed. Source review confirms the release includes earlier secure-account/group changes relative to production, not just the latest frontend slice. Production auto-deploys main without waiting for CI; no push, deployment or setting change was made. Source commit, remote build and final cutover checks remain pending. No budget increase is needed to resolve the download issue.


## Verified off-volume backup — 9 September

User downloaded store-and-keys.tar.gz to Downloads. Copied and safely extracted it under the current task's private work/release-backup-20260909 directory, outside the repository. Archive SHA256 c22535eca6a47f38544d673da50403ade2997df4cadc8a059663078554d04516 matches the prior Railway console output. Validated v5 store SHA256 bc329fa45ad42799002348798afa0020dcd10241c3b85a874b04da7b32b52671: 2 events, 9 days, 217 performances, 6 users, 199 selections, 3 push devices, no groups/memberships/deliveries. One key file is present. Exclusive restore-copy verification passed.

The release publish started against the isolated restored JSON with fresh local test keys and push disabled. Health returned ok with expected event/user/selection counts; POST session returned 503 under read-only mode. Startup normalized the isolated v5 copy to v7 and preserved a byte-identical before-v5-upgrade backup. The original extracted snapshot remained unchanged. Local server stopped. Existing production session-cookie continuity was not tested; the archived production key was not used by the test host.

Off-volume preflight verification is complete. This is still a snapshot taken before the final maintenance window; fresh final backup and remote build/smoke checks remain required. No production deployment or paid resource changes.
