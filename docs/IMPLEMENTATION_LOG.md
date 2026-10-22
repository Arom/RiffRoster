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


## Production deployment — 9 September 2026

User explicitly approved the final backup check and updating main. Immediately before the push, Railway console confirmed the live store and keys matched the previously verified off-volume snapshot byte for byte (VERIFIED_BACKUP_STILL_CURRENT). The service remained writable during this comparison; no maintenance-window freeze is claimed. A normal fast-forward pushed main from c2ed125 to 6ec84b8b8643e8f7624c12726c1eb1d1c2bc4ba5.

Railway deployment 06a1d665-7f3e-4206-bbf9-adc30292620a built successfully and became Active. The former deployment cf412b54-0050-4aad-b62b-4b5ae03b9ecc is retained in history. A request during the transition returned 502; after activation health returned ok, with 2 events, 6 users and 199 selections, and pushConfigured true. The generated frontend bundle returns 200, the shell references it, and unauthenticated state returns 401. The new private-account login screen renders without captured browser errors.

Production store reports schema v7. The before-v5-upgrade backup is byte-identical to the verified off-volume source, and the data-protection key directory is unchanged (UPGRADE_BACKUP_MATCHES_VERIFIED_COPY, KEYS_PRESERVED). No manual restore or data overwrite occurred. One existing service/replica/volume remains; no PostgreSQL cutover, paid service or budget increase.

Authenticated production pick/admin/group journeys and actual push delivery remain user-assisted smoke checks: no account credentials were supplied and no test account was added to production. Equivalent synthetic journeys passed remote CI on JSON and PostgreSQL. Do not claim public launch or the complete Phase 3 finished. Runtime request bodies/controller migration and broader public-launch work remain open. These documentation updates are local only to avoid another automatic deployment.

## Login cache compatibility fix — 9 September

The reported app.js:88 trace matches the former production script reading formData.get('username').trim(), while the current HTML supplies loginName/displayName. This indicates mixed frontend versions from stale asset caching. Commit a238bae versions CSS, the legacy controller and the React bundle by content hash in both HTML and service-worker precache entries. Docker copies the stamped HTML from the frontend stage. The current login handler also guards missing fields and displays a refresh message instead of an uncaught exception. No account or data schema changes.

Validation: 20 Node checks, 11 Chromium journeys, strict typecheck and .NET build passed; cache stamping is stable on repeated runs. The regression blocks the unversioned script URL and checks missing-field handling and recovery on reload. An initial redundant sign-in in the test hit the unchanged rate limiter and was removed. The fix was pushed to main and the feature branch for the existing Railway service; live verification follows.

## Production login confirmation - 10 September

Live verification found /app.js?v=95c03dff164d6c73 in production HTML, the login guard in that script and the matching service-worker entry. Health remained ok with 2 events, 6 users and 199 selections. The user subsequently confirmed successful login. This closes the reported login crash; production picks/admin/groups and actual push delivery were not independently exercised.

## Phase 3F - Typed request construction

All legacy controller API calls now pass domain-shaped values or IDs to typed endpoint methods, which select the HTTP verb/path and construct JSON. New requests.ts mirrors current C# request contracts and checks the JavaScript bridge: strings must be strings, route IDs nonblank, bulk rows and nested push keys the expected shape, and notification preferences supported values. Serialization copies only contract fields and never invokes caller toJSON. Password whitespace and explicit time offsets are preserved. Server business validation remains authoritative. Generated contracts and full controller migration remain open.

Validation: 23 Node checks passed, including request method/path/body coverage, rejection before fetch and unknown-field exclusion. All 11 Chromium journeys passed against an isolated JSON test server, including login, picks/reload, crew membership, event/act edits, poster import, notification mocks and asset compatibility. Strict TypeScript and .NET build passed with zero build warnings/errors. Git whitespace checks passed. Bundle: 205.33 kB raw / 64.88 kB gzip.

No migration, backend behavior, dependency or Railway resource changes. This slice is local and uncommitted; production remains on a238bae. Rollback restores the controller, generated bundle, stamped index and matching service worker together. Backend integration suites and production deployment were not repeated for this frontend-only slice. Next: remaining controller state/actions migration, with generated contracts and offline event/outbox work still outstanding.

## Phase 3G - Typed schedule state transitions

Added scheduleState.ts for immutable reconciliation of validated pick, event, saved-act and deleted-act results. Repeated selected responses do not duplicate picks, stable-ID edits preserve selections when moving between days, and missing destination days return the original snapshot for reload. Calendar-day calculation uses explicit timestamp date with UTC calendar arithmetic, preserving the server's 06:00 festival-day boundary without device-timezone dependence. Legacy save/delete handlers now capture the originating event before awaiting the response, preventing reconciliation against a subsequently selected event. They do not switch the visible day when another event is active.

Validation: 29 Node checks and 11 Chromium journeys passed, with strict TypeScript, .NET build (zero warnings/errors) and whitespace checks. Six new tests use frozen input snapshots and cover idempotent picks, stable-ID moves, missing-day fallback, targeted deletion, leap/year boundaries and repeated event responses. Bundle 206.56 kB raw / 65.25 kB gzip. Browser journeys continue to verify retained picks through admin edits/reload. No new backend tests or real-device push tests were run.

Files: src/RiffRoster.Web/src/app/scheduleState.ts and entry.ts, wwwroot/app.js, stamped index.html/service-worker.js, tests/frontend/schedule-state.test.cjs and plan/log. Prior Phase 3F work is preserved. No schema or infrastructure changes; changes remain local and uncommitted. Rollback restores the matching frontend source/bundle and stamped shell assets. Full controller state, navigation and async session lifecycle migration, generated contracts and offline cache/outbox remain outstanding.

## Phase 3H - Navigation and event-specific crew preferences

Phase 3H local progress (10 September): typed navigation now resolves event/day fallback, validates selected groups and supplies crew context. Comparison preferences are scoped by account, event and group in a v2 local-storage namespace. Existing group-only choices are used when no event-specific value exists; empty choices are retained and former members filtered. Event switches, creation and notification deep links refresh the comparison context. Validation: 34 Node checks and 11 Chromium journeys passed, including switching events and reloading comparison choices; strict TypeScript, .NET build and whitespace checks passed. No server migration, deployment or additional Railway budget. Async session lifecycle, remaining controller migration, generated contracts and offline event cache/outbox remain open.

New navigation.ts contains typed event/day resolution, group/context selection and storage preference parsing. Browser storage remains untrusted; corrupt or inaccessible values use current crew defaults, while explicit empty arrays remain empty. Legacy preference keys are retained, allowing rollback to the prior group-only behavior without deleting preferences. New per-event choices are not read by older clients. No schedule, account or server data changes.

Files: navigation.ts, entry.ts, wwwroot/app.js, stamped index.html/service-worker.js, tests/frontend/navigation.test.cjs, extended crew browser journey, and plan/log. Bundle: 207.71 kB raw / 65.62 kB gzip. Prior local Phase 3F/3G changes remain intact. Everything remains local and uncommitted; production stays on a238bae. Restore matching frontend source/bundle and shell assets for rollback. Backend integration and real-device push delivery were not retested for this frontend slice.

## Phase 3I - Snapshot and authentication ordering

Phase 3I local progress (10 September): a typed session lifecycle guard orders snapshot reads and invalidates outstanding reads when authentication starts. Only the newest load or group refresh may update state or report an error. Sign-in/sign-out cookie-changing operations are exclusive; sign-out clears in-memory event/day/group/filter context. Validation: 37 Node checks and 11 Chromium journeys passed, including a held pre-logout snapshot released after logout that must leave the login screen visible. Strict TypeScript, .NET build and whitespace checks passed. No schema change, deployment or added Railway budget. This covers snapshot/authentication coordination only; mutation and notification callback lifecycle handling remain outstanding.

Added sessionLifecycle.ts and exported it through entry.ts. loadApp and refreshAfterGroupChange share latest-read ordering; both discard stale successful snapshots and stale failures. Authentication generation changes permanently invalidate earlier reads. An old authentication completion cannot unlock a newer pending operation. No request cancellation or automatic retry is introduced, and server-side session behavior is unchanged.

Files: sessionLifecycle.ts, entry.ts, wwwroot/app.js, stamped index.html/service-worker.js, session-lifecycle.test.cjs, the extended state-recovery browser test, and plan/log. Bundle: 207.99 kB raw / 65.75 kB gzip. Existing Phase 3F-H changes are preserved, local and uncommitted. Production remains on a238bae. Rollback restores matching controller, generated bundle and shell assets; no data migration. Pending mutation/notification callbacks and cross-tab authentication are outside this slice; broader controller, generated contract and offline outbox work remains open.

## Phase 3J - Pending action session guards

Phase 3J local progress (10 September): typed action-generation tokens guard pending schedule/group mutations and notification workflows across authentication transitions. Stale successes and errors are discarded, and notification setup rechecks the session after permission, service-worker and subscription awaits before continuing. Browser coverage holds a committed pick response until after logout and verifies application data remains null without uncaught errors. Validation: 38 Node checks, 11 Chromium journeys, strict TypeScript, .NET build and whitespace checks passed. No migration, deployment or additional Railway budget.

Action tokens remain valid through ordinary snapshot reads but expire permanently when authentication starts. Legacy mutation handlers check before applying results or rendering errors, including follow-up reloads. Notification enable/disable/preference/test and refresh steps stop when the originating session is no longer current. Requests already sent may commit server-side; there is no cancellation, automatic retry or cross-tab guarantee. A browser subscription created during a transition may remain on the device; this slice prevents subsequent stale setup/UI work but does not add compensating subscription cleanup.

Files: sessionLifecycle.ts, wwwroot/app.js, matching index.html/service-worker.js fingerprints, session-lifecycle.test.cjs, extended state-recovery browser test, plan/log. Bundle: 208.05 kB raw / 65.77 kB gzip. Prior local work remains intact. Production remains on a238bae and all newer slices are local and uncommitted. Rollback restores matching frontend assets. Remaining work includes cross-tab authentication, subscription lifecycle cleanup, full controller migration, generated contracts and offline cache/outbox; real push delivery remains unverified.

## Phase 3K - Cross-tab sign-out

Phase 3K local progress (10 September): cross-tab sign-out invalidation uses BroadcastChannel plus a storage-event fallback, transmitting only a fixed signal/random marker. Receiving tabs invalidate reads/actions/authentication completions, clear app context and close open dialogs. Validation: 41 Node checks and 11 Chromium journeys passed, including a second tab cleared on logout alongside delayed-response protection. Strict TypeScript, .NET build and whitespace checks passed. No migration, deployment or additional Railway budget. Simultaneous cross-tab sign-in coordination and subscription cleanup remain open.

Added sessionChannel.ts, lifecycle invalidation/authentication-token checks and shared clearSessionView. Logout clears other open same-origin tabs without sending credentials, usernames or schedules. Receiving tabs do not repeat server logout or broadcast again. Incoming malformed signals are ignored. If both browser transports are unavailable, local sign-out still works; cross-tab synchronization is then unavailable. Server logout failure behavior remains unchanged: local views clear, but that is not proof of server-side revocation. Cross-tab cookie-changing operations are not globally serialized.

Files: sessionChannel.ts, sessionLifecycle.ts, entry.ts, wwwroot/app.js, matching index.html/service-worker.js fingerprints, channel/lifecycle unit tests, the extended browser journey, plan/log. Bundle: 208.56 kB raw / 65.98 kB gzip. Prior slices remain local and uncommitted, with production still on a238bae. Rollback restores matching frontend assets; no persisted server data changes. Generated contracts, broader controller migration, offline cache/outbox and real-device push verification remain unfinished.

## Session channel review follow-up

Session-channel follow-up (10 September): successful sign-in now invalidates sibling tab views as well as sign-out. Both transports carry the same opaque marker, deduplicated with a bounded 128-marker history so a second delivery does not repeat invalidation. Storage-only delivery is unit-tested. Validation: 42 Node checks, 11 Chromium journeys, strict TypeScript, .NET build and whitespace checks passed. Bundle: 208.65 kB raw / 66.02 kB gzip. Existing browser coverage includes two-tab logout; a distinct-account switch was not separately browser-tested. Concurrent cross-tab cookie writes are still not serialized.

Changed sessionChannel.ts, the sign-in/sign-out callers in wwwroot/app.js, session-channel.test.cjs, matching shell fingerprints and documentation. The channel carries no identity or credentials. Malformed/duplicate markers are ignored. Local authentication remains usable when transports are unavailable; sibling synchronization is then unavailable. Protocol changes apply to these still-unreleased local slices, not an existing production channel. No migration, deployment or budget change. Prior local changes remain intact and uncommitted; production remains a238bae. Rollback restores matching frontend assets. Subscription cleanup, remaining controller/generated-contract and offline cache/outbox work remain open.

## Phase 3L - Late browser subscription cleanup

Phase 3L local progress (10 September): typed browser subscription acquisition checks the originating session after lookup and creation. A subscription returned by a creation attempt after that session expires is unsubscribed on a best-effort basis and never sent to the server; a subscription found by the initial lookup is preserved. Validation: 46 Node checks and 11 Chromium journeys passed; the notification journey was then extended with delayed creation after logout and passed a targeted rerun. Strict TypeScript, .NET build and whitespace checks passed. No migration, deployment or additional Railway budget. Simultaneous subscription setup across tabs is not serialized and remains a release limitation.

Added pushSetup.ts, exported through entry.ts and used by enableNotifications. Tests cover reused subscriptions, expiry during lookup/creation, cleanup failure and active creation errors. The browser fixture delays subscribe until logout completes, then verifies unsubscribe runs and no subscription POST occurs. Existing server registrations and failures after server persistence are outside this acquisition-only slice. Browser subscribe may be shared across tabs; concurrent setup/ownership must be coordinated before treating cleanup as a complete multi-tab guarantee.

Files: pushSetup.ts, entry.ts, wwwroot/app.js, matching index.html/service-worker.js, push-setup.test.cjs, notification browser journey, plan/log. Bundle 208.86 kB raw / 66.09 kB gzip. Prior work remains local and uncommitted; production remains a238bae. Rollback restores matching frontend assets, with no server data changes. Generated contracts, remaining controller migration, offline cache/outbox and actual device push verification remain open.

## Notification acquisition concurrency

Notification acquisition follow-up (10 September): cooperating setup attempts now serialize lookup, creation and stale cleanup through the same Web Lock. Each attempt checks session validity after acquiring the lock. Without Web Locks, stale creation results are discarded without unsubscribing a potentially shared subscription. Validation: 48 Node checks, 11 Chromium journeys, strict TypeScript, .NET build and whitespace checks passed. Bundle: 209.02 kB raw / 66.15 kB gzip. The lock covers acquisition attempts only; automatic refresh/persistence, disable and logout are not jointly serialized with them, so complete subscription ownership coordination remains open.

Changed pushSetup.ts, push-setup.test.cjs and matching bundle fingerprints in index.html/service-worker.js, plus plan/log. The concurrency unit test uses two independent client contexts and a shared lock queue to verify a newer acquisition waits until stale cleanup finishes, then creates a usable subscription. Existing browser notification coverage passed using the browser lock, including delayed creation after logout. Browsers without locks retain abandoned browser subscriptions rather than risk removing another tab's subscription. No migration, deployment or budget changes; all recent work remains local and uncommitted, production a238bae. Rollback restores matching frontend assets. Broader notification coordination, generated contracts and offline cache/outbox remain open.

## Phase 3M - Coordinated notification and authentication operations

Phase 3M local progress (10 September): notification setup now holds the shared Web Lock through server persistence and UI refresh. Automatic subscription refresh, preference saves, disable/test operations and sign-in/sign-out use the same lock; internal refresh functions avoid recursive locking. Permission prompts remain outside the lock. Queued notification work checks both the local session generation and the stored session-change marker before starting. Validation: 50 Node checks, 11 Chromium journeys, strict TypeScript, .NET build and whitespace checks passed. The delayed-creation regression now queues refresh and logout together and verifies cleanup, no stale server save and completed logout. No schema migration, deployment or additional Railway budget.

Updated pushSetup.ts with withPushOperation and an explicit held-lock acquisition path. runNotificationOperation checks queued actions before invoking unlocked notification handlers; nested refresh stays within the existing lock. Authentication changes are published while holding the lock, and the channel exposes its storage revision for queued work to inspect. This closes the previously documented gap between acquisition-only locking and refresh/persistence/removal/authentication in cooperating updated tabs.

Files: pushSetup.ts, sessionChannel.ts, entry.ts, wwwroot/app.js, matching shell fingerprints, push-setup tests, notification browser journey and plan/log. Bundle: 209.16 kB raw / 66.20 kB gzip. Unit tests verify that persistence retains the outer lock without nested acquisition and failures are not retried. Existing two-tab invalidation and delayed snapshot/pick journeys continue to pass.

Limits: Web Locks provide cross-tab serialization only where available and only for cooperating code. Without them, existing stale-action guards still apply but tabs are not serialized and stale acquisition cleanup is skipped. In-flight browser/network operations can delay lock acquisition and logout completion; no timeout/cancellation was introduced. Server logout failures still clear local views and are not proof of revocation. Real-device push delivery, generated API contracts, broader controller migration and offline cache/outbox remain open. No production changes; recent work remains local and uncommitted on top of a238bae. Rollback restores matching frontend assets with no server data migration.

## Phase 3 foundation acceptance — 11 September 2026

Completed the remaining current-API foundation in one acceptance pass: generated request/endpoint client and OpenAPI request artifact; IndexedDB account/event cache and explicit-pick outbox; idempotent selection endpoint with mutation acknowledgement and event-content version; reconnect reconciliation; authenticated event search/preview and profile; lazy OCR; deferred shell execution with offline precache; accessibility, enlarged-text and keyboard fixes. Preserved the earlier session, push and typed state work. No server schema migration or Railway budget change.

Validation: 50 frontend checks, 59 backend checks, 12 Chromium journeys on each adapter, strict TypeScript, build/whitespace checks, 3 backup-tool tests and Release publish passed. Final local Lighthouse scores: 100 performance / 100 accessibility / 100 best practices. Chromium manifest/installability checks passed. Direct keyboard review covered login through pick/My route and logout. Offline tests cover lost acknowledgements and a newer queued action during reconnection. Fixed the local PostgreSQL port mismatch and stale provider test-host build; neither was a production failure.

The budget-track Phase 3 foundation is complete locally. Full public identity and anonymous discovery remain explicitly gated on Phase 2/5 authorization. Existing JavaScript compatibility UI remains; generated response schemas and an all-TypeScript controller are not claimed. Real-device push and public-release validation remain separate. Production is unchanged at a238bae, and the working changes remain uncommitted/unpushed. See PHASE_3_ACCEPTANCE.md for exact scope, evidence and combined API/frontend rollback considerations.

## Phase 4A — Personal route calendar export

Checklist: implement a typed local calendar serializer; expose it from My route; verify account/event isolation, overnight offsets, escaping/folding and offline download; preserve current storage and hosting contracts.

Implemented a local .ics download containing the current account's picks for the selected festival across every day, independent of search/stage filters. Uses UTC times, stable event/performance UIDs, CRLF content lines, escaped TEXT fields and UTF-8 byte-aware folding per RFC 5545 (https://www.rfc-editor.org/rfc/rfc5545.html). Includes artist, stage and location but no account identity, crew membership, alarms or invitations. The UI explains that exported entries are a snapshot and do not update automatically. Empty routes produce actionable feedback; object URLs are released after the download starts.

Validation: strict TypeScript/build and 54 frontend checks passed. Four calendar checks cover account/event isolation, overnight dates, Unicode/property-injection handling, stable IDs, safe filenames and invalid/empty input. The browser acceptance journey also downloads and inspects the file with networking disabled. No backend contract, persisted schema, package dependency, Railway service or budget change. Production remains unchanged; work is local and uncommitted. Remaining Phase 4 scope includes group roles/invites, priorities, cross-device preferences, live updates and richer consensus views.

## Phase 4B — Explicit group ownership transfer

Checklist completed: reuse existing owner field; enforce owner/member authorization at the transactional storage boundary; add a typed generated-client mutation and eligible-member UI; test immediate permission changes, retained membership and restart persistence on both providers; review enlarged-text controls.

Added PUT /api/groups/{groupId}/owner with TransferGroupOwnerRequest. Missing/unauthorized groups and ineligible recipients return the same forbidden result; anonymous requests require authentication. Global administrator status does not bypass group ownership. The request uses the authenticated principal, resolves recipient casing from membership and preserves all memberships and the existing join-code hash. After handover the old owner cannot rotate codes or take ownership back without a transfer from the new owner. Existing leave/automatic-succession behavior remains in place.

Generated client now contains 14 request contracts and 22 methods. The response decoder checks group and recipient identity and membership before updating the UI. The frontend captures session generation, offers current members only, confirms the named handover, prevents duplicate button submission and removes owner controls after success. Group member text wraps at enlarged text size; two malformed dialog-close glyphs were corrected.

Validation: 55 frontend checks, 31 characterization tests, 14 PostgreSQL integration tests and 12 Chromium journeys per adapter passed; strict typecheck, solution build, whitespace checks and Release publish passed. The PostgreSQL test restarts DataStore and verifies old/new permissions. The browser journey performs a two-account handover and reloads the recipient. Review image: work/phase4-owner-transfer-text-zoom.png. An initial aggregate browser run hit the unchanged shared account-action rate limit; browser groups now run on fresh isolated hosts. No rate-limit relaxation. Final dialog glyph changes passed the two affected browser journeys.

No persisted shape changes: CreatedBy already stores the current group owner, including prior automatic succession. Existing JSON/PostgreSQL migration and reverse-export mappings therefore remain applicable. No additional Railway budget or service, no production deployment. All work remains local alongside the earlier uncommitted implementation. Phase 4 remains open for richer roles/invites, priorities, consensus, shared filters and live invalidation.
