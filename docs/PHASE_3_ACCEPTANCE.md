# Phase 3 acceptance — 11 September 2026

The current-API Phase 3 foundation is complete locally. The full public-facing phase remains gated on the identity and visibility capabilities explicitly deferred by the budget-track execution order. Production remains at a238bae; this work is uncommitted and has not been pushed or deployed. No Railway service, replica, database or budget was added.

## Deliverable ledger

| Deliverable | Result |
| --- | --- |
| React/TypeScript feature structure | Strict typed timeline/day components, transport, runtime state/mutation validation, request serialization, navigation, reconciliation, session coordination and offline storage. Existing JavaScript DOM/dialog controller remains as a compatibility layer. |
| Generated API client | Build derives 13 request contracts and 21 endpoint methods from the current C# contract/endpoint declarations. OpenAPI request artifact is checked in. Response payloads remain unknown at transport and are validated by explicit runtime decoders; generated response models are not claimed. |
| Design system | Colour, font, spacing, radius, elevation, focus and motion tokens; stage colours; contrast and touch-target fixes; wrapping header/group controls at enlarged text. |
| Authenticated shell/search/profile | Existing private account flow preserved. Deferred profile and searchable event previews show location, dates, counts and artist examples. Event opening retains existing navigation. |
| Public auth/discovery | Deferred: Phase 2 identity/recovery and Phase 5 visibility authorization must precede new public flows. Authenticated previews do not expose the catalogue anonymously. |
| Schedule and My route | React cards/day tabs, stable performance IDs and focus, keyboard day navigation, native touch scrolling and arrow-key timeline scrolling. |
| Offline state | Validated IndexedDB snapshots per account/event, selections/group summary/schedule fingerprints, account-scoped explicit pick outbox, queued/error/retry states and cache availability feedback. Generic HTTP cache excludes API responses. |
| PWA and payload | Hashed shell, service worker, manifest, install flow and notification deep links retained. Profile/search is separately executed on demand and its small bundle is precached for first offline use. OCR loads only when import opens. |

## Verification

- `pnpm test`: 50 checks passed; strict TypeScript and production frontend build passed. Main bundle 214.66 kB raw / 67.63 kB gzip; deferred shell 1.62 kB / 0.87 kB gzip. OCR is separate.
- `dotnet test RiffRoster.sln --no-restore`: 59 passed — 6 domain, 8 application, 2 architecture, 30 HTTP characterization, 13 PostgreSQL integration. Disposable localhost PostgreSQL only. An initial port mismatch was corrected; the passing run used port 5432.
- `pnpm test:e2e`: 11 regression journeys plus one Phase 3 acceptance journey passed against JSON and PostgreSQL. The command rebuilds the solution to avoid stale provider test hosts and starts a fresh host for the acceptance group to respect the unchanged login rate limit.
- Offline coverage: load cached event with the browser network disabled, change a pick, reload, simulate a lost response after server commit, retry without toggling twice, preserve a newer queued action while an older acknowledgement is held, and open profile on first offline use. Cache clearing on logout is checked.
- Accessibility: axe WCAG 2 A/AA, 2.1 AA and 2.2 AA tags report no violations for login, schedule, profile, search and enlarged-text views. This is scoped automated coverage, not a certification of every possible state.
- Direct keyboard review in the in-app browser: Tab/Shift-Tab sign-in, profile opening and Escape focus return, event search/preview and selection, arrow-key day navigation, Space pick with retained focus, My route, Compare and profile sign-out. Visible focus inspected. The 200% text screenshot was reviewed for clipping and overlap.
- Chromium DevTools reports zero manifest errors and zero installability errors. Actual installation on every mobile platform and real push delivery remain release/device checks.
- Final local mobile Lighthouse: Performance 100, Accessibility 100, Best Practices 100, SEO 90; no warnings. Production-built frontend assets, authenticated seeded schedule, synthetic account, local Development host, simulated mobile throttling and warm storage. This does not measure Railway network latency or certify production performance.
- Release publish, six matching published frontend asset hashes, solution whitespace verification, `git diff --check`, and three Python backup-tool tests passed. Docker image execution was not tested; Docker CLI is unavailable. Docker now copies the stamped controller as well as generated bundles/index/service worker.

Audit artifacts are in ignored local `work/phase3-final-audit/`; the reviewed enlarged-text image is `test-results/phase3-text-zoom.png`. A copy of the acceptance report and audit evidence is in the active task's outputs directory.

## Sync and release contract

`PUT /api/selections/{performanceId}` accepts explicit `selected` and a bounded optional `mutationId`, uses the authenticated canonical account, and returns accepted selection, mutation ID, event ID and schedule fingerprint. Repeating an explicit state does not duplicate or toggle a pick. The fingerprint hashes the current event structure; it is a content version, not a monotonic edit counter. State snapshots include the corresponding event-version map. No persisted server schema changes are required.

The outbox coalesces the latest local choice per account/performance. Acknowledgements only remove the matching local mutation ID; newer local choices survive reconnect. Missing performances are dropped with a visible notice, and the refreshed server schedule takes precedence. Failures retain pending work and expose retry. This is not a server-side historical mutation journal or a cross-device timestamp ordering guarantee; independently submitted device actions use server arrival order. Those stronger collaboration semantics belong to the later collaboration phase.

Ship frontend and API together on the existing single-replica service. The new outbox requires the explicit-state endpoint and acknowledgement contract. Older servers cannot synchronize it. Rollback must restore a matching API/frontend bundle and should first drain outstanding browser outboxes; do not silently clear unacknowledged local picks. Server data remains compatible with the prior schema. Storage eviction, private-browser restrictions or signing out can remove local offline state; the UI reports when its offline copy is unavailable.

## Remaining gates

1. Full public identity/recovery and anonymous event discovery/preview: implement their deferred Phase 2/5 server authorization first. No budget increase is currently established as necessary.
2. Production release and authenticated smoke checks for this combined change; it is currently local and reviewable.
3. Real-device installation, screen-reader coverage and real push delivery remain public-launch validation; automated Chromium checks do not substitute for those device results.

The historical 3A–3M entries are an implementation trail, not separate new phases. Future work should use this ledger instead of treating the already finished foundation as another open sequence of tiny slices.
