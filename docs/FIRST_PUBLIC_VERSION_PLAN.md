# RiffRoster: first public commercial version

Status: implementation specification  
Prepared: 7 September 2026  
Budget revision: 8 September 2026 — no additional Railway budget authorized  
Target implementer: GPT-6 Astra, Medium reasoning  
Target release: public PWA beta, followed by a paid launch after one festival pilot

## 1. Executive decision

### Current delivery constraint: no additional Railway budget

Continue development using the existing Railway service, its existing volume and one replica. Do not upgrade the Railway plan, add paid services/workers/storage, or increase resource allocations without new authorization. Do not substitute a paid service on another provider. Existing usage charges can still fluctuate; this constraint is not a promise that traffic has zero cost.

Production remains on JSON until a separately validated storage change fits the existing budget or the user authorizes more spending. Preserve the implemented PostgreSQL adapter, migrations, comparison/export tooling and backup drills for later use. PostgreSQL deployment is deferred, not a prerequisite for further feature implementation. The production-copy rehearsal passed on 8 September; no production database cutover is claimed.

Use disposable local environments for integration, browser, migration and rollback checks. Hosted staging, a separate backup worker, managed database services and a Railway plan upgrade are deferred. Local verification permits development to advance; it does not certify production deployment, high availability or public-launch readiness.

Only notify the user that more budget is required when a concrete next step cannot meet its correctness, security, recovery or measured capacity requirements on existing resources and reasonable no-additional-cost alternatives have been evaluated. Report the blocked feature, evidence, alternatives tried and smallest costed change. Continue independent work while that decision is pending. Do not request an upgrade merely because the original target architecture named PostgreSQL, Pro or hosted staging.

The budget rules and phase sequencing below override conflicting infrastructure prerequisites elsewhere in this specification. Product/security acceptance criteria remain in force; defer release of a dependent feature instead of weakening those criteria.

Build RiffRoster as the fastest way for a group to turn a festival poster into a shared, clash-aware plan.

The first public version should remain an installable web application (PWA). Do not build Android or iOS binaries yet. A link is dramatically easier to share in a group chat than an app-store install, the current application already has a service worker and Web Push, and iOS supports standards-based Web Push for Home Screen web apps from iOS 16.4. Native apps should be considered only after usage data shows that PWA installation, notification reliability, or offline behaviour is materially reducing retention.

The business should not charge ordinary attendees for the basic shared schedule. That would obstruct the invitation loop on which the product depends. Monetise the people who create and maintain schedules, then sell verified communication and analytics to festival organisers.

The recommended positioning is:

> Upload the running-order poster. Fix anything the scanner missed. Invite your crew. See every clash and who will be at each set.

The defensible workflow is not merely another timetable. It is the combination of:

1. poster-to-editable-schedule creation;
2. excellent time-scaled clash visualisation;
3. group consensus and split visibility;
4. reliable offline access and reminders on the festival site;
5. schedule corrections that retain every existing pick.

## 2. Product and commercial thesis

### Beachhead market

Start with European metal, punk, rock, and specialist multi-stage festivals. This is a useful beachhead because the current product and seed data already fit the audience, communities are concentrated in festival-specific Facebook/Reddit groups, and complicated multi-stage lineups make the problem obvious.

The initial ideal user is the unofficial schedule organiser in a group of 4–12 friends. They receive a poster or running order, create the event, correct it, and share a link. Other members should obtain value in under 60 seconds.

### Competitive gap

Clashfinder already provides free community-created timetables and a donation-based premium tier. Newer products also advertise friend overlays, priorities, schedule imports, or group coordination. RiffRoster therefore cannot win by offering a timetable and stars alone. It must be noticeably faster from poster to shared group plan and noticeably clearer when a group will split or reunite.

The product promise for launch is measurable:

- A competent user can turn a legible poster into a reviewable draft in under three minutes.
- A friend can join a group and make their first pick in under one minute.
- Every clash is visible without opening a detail page.
- A schedule change preserves picks because performances have stable identifiers.
- A previously opened event remains usable with poor or absent connectivity.

### Revenue model

Keep attendee functionality free: browsing, personal picks, groups, comparison, offline schedule, and reminders. These are the acquisition engine.

Offer two paid products:

#### Community Creator — EUR 29/year

For people who maintain unofficial schedules:

- unlimited published and private events;
- multiple schedule editors;
- poster OCR and CSV import beyond the first free event;
- duplicate-event workflow and reusable stages;
- edit history and rollback for 30 days;
- creator profile and attribution;
- JSON/CSV export;
- priority support.

Every account receives one complete event free. This lets users experience the full creation loop before paying and continuously adds searchable schedules.

#### Verified Festival — launch pilot EUR 249/event

Sold manually at first; do not build a self-service enterprise sales system.

- verified organiser badge and canonical event ownership;
- official schedule-change broadcasts;
- festival colours, logo, links, venue information, and emergency contact information;
- embed/QR link for the festival website and printed material;
- aggregate, privacy-preserving popularity and clash analytics;
- CSV import and assisted setup;
- audit log and two organiser seats;
- email support during the event.

After five paid pilots, replace pilot pricing with public tiers based on attendee volume, for example EUR 299/event up to 5,000 active planners and EUR 699/event up to 25,000. Validate willingness to pay before fixing those prices.

Do not add advertising in version one. It would reduce timeline clarity and add consent/privacy complexity before there is meaningful traffic. Do not sell individual taste data. Organiser analytics must be aggregated and suppress small cohorts.

### Plausible path to profitability

The early business is a low-infrastructure, owner-operated product, not a venture-scale consumer subscription on day one.

Illustrative annual outcome after validation:

| Source | Quantity | Price | Gross annual revenue |
|---|---:|---:|---:|
| Community Creator | 250 | EUR 29 | EUR 7,250 |
| Verified Festival | 40 | average EUR 399 | EUR 15,960 |
| Total | | | EUR 23,210 |

This is not a forecast. It is a target model to test. With a small .NET/PostgreSQL workload, payment processing, email, monitoring, and modest support costs, it can be profitable as a focused side business. Meaningful full-time revenue requires either hundreds of organiser customers, materially higher organiser pricing, or adjacent event types.

Track contribution margin per festival. Include hosting, payment fees, email, support time, refund/chargeback loss, and any manual schedule setup. Stripe's current standard Hungarian price for a standard EEA online card is 1.5% + HUF 85; this should be checked again before launch.

## 3. Publish as PWA first; define the native-app gate

### Why the PWA is the right first release

- Invitations open directly from WhatsApp, Messenger, Discord, or Reddit.
- One responsive codebase serves desktop schedule creators and mobile attendees.
- Web payment through Stripe avoids app-store billing integration during validation.
- Standards-based Web Push works on Android browsers and on iOS Home Screen web apps.
- The existing RiffRoster implementation already has a manifest, service worker, and push subscription flow.
- Releases are immediate, important when running orders change shortly before a festival.

Make installation optional. The app must be useful in the browser; ask users to install only when installation gives them a clear benefit such as locked-screen reminders and offline access. On iOS, show a platform-specific explanation that notifications require adding the site to the Home Screen.

### PWA limitations that the UX must acknowledge

- iOS Web Push requires a Home Screen web app; an ordinary Safari tab is insufficient.
- Operating systems may delay delivery for power, network, or focus-mode reasons. Notifications are helpful reminders, never a guaranteed alarm.
- A newly opened event cannot be used offline until its data and application shell have been cached.
- Users can clear site data or revoke notification permission outside the application.

### Native-app decision gate

Review native development after two complete festival seasons or 10,000 monthly active users, whichever comes first. Start native work only if at least one is true:

- fewer than 35% of returning mobile users successfully install the PWA after seeing the contextual prompt;
- more than 5% of active users report missed reminders attributable to PWA delivery rather than OS settings or lack of connectivity;
- organiser prospects repeatedly require App Store/Google Play presence to purchase;
- offline startup or background refresh remains a top-three support issue;
- native calendar, widgets, Live Activities, or richer background scheduling is shown to improve retention.

If the gate is met, preserve the ASP.NET Core API and build a Capacitor-based shell around the TypeScript front end first. Add genuine native value: local notification scheduling, background schedule refresh, app links, share targets, calendar export, widgets, and secure credential storage. Do not submit a plain WebView. Apple explicitly requires apps to exceed a repackaged website, and Google requires meaningful, stable mobile functionality.

## 4. Version-one scope

### Required attendee capabilities

1. Discover a public event by search or open an invitation link.
2. Preview a public schedule without creating an account.
3. Create an account or sign in, then return to the exact event/group context.
4. Make personal picks in three states: `Must see`, `Interested`, and `Not selected`.
5. View the running order as a time-scaled, horizontally scrollable stage timeline.
6. View `My route` as the same time axis, including adjacent clashes and travel warnings.
7. Create, join, switch, rename, and leave groups.
8. Invite through a deep link; codes remain available as a fallback.
9. See avatars/names and a crew heat indicator directly on performance cards.
10. Compare any selected group members, with the choice remembered per event/group.
11. Receive opt-in performance reminders, optionally including attending friends.
12. Install the PWA and use already-opened schedules offline.
13. Export personal picks to an `.ics` calendar file.
14. Edit display name and password, export personal data, and delete the account.

### Required creator capabilities

1. Create an event as `Private`, `Unlisted`, or `Public`.
2. Enter event name, venue/location, timezone, dates, and optional official URL.
3. Import a schedule from an image entirely in the browser. The image must not leave the device or persist after the review workspace closes.
4. Keep the poster visible beside an editable table during review.
5. Import from a documented CSV template.
6. Add/edit/delete/reorder stages and performances manually.
7. Detect invalid times, duplicates, out-of-range dates, impossible durations, and likely OCR mistakes before publication.
8. Publish a draft atomically; attendees never see a half-imported schedule.
9. Edit an existing performance without changing its ID, preserving all picks.
10. Cancel or delete a performance with an explicit choice: retain as cancelled, move picks to a replacement, or permanently remove.
11. Invite additional editors on a paid event.
12. View edit history and restore a prior schedule version on paid events.

### Required platform/admin capabilities

1. Search, inspect, unpublish, or delete events.
2. Suspend accounts and revoke sessions.
3. Merge duplicate public events while preserving selections and group links.
4. Transfer event ownership.
5. Grant/revoke Creator and Verified Festival entitlements.
6. Inspect payment/webhook status without seeing card details.
7. View an audit trail of destructive and administrative actions.
8. Configure a banner for maintenance or known notification problems.

### Explicitly excluded from version one

- public social feeds, chat, direct messaging, or friend discovery;
- precise/live location sharing;
- retained poster uploads or automatic scraping of festival websites;
- Spotify/Apple Music account access;
- ticket sales or resale;
- recommendations generated from personal listening history;
- native Android/iOS binaries;
- advertising marketplace;
- complex white-label applications;
- multi-language content beyond making the UI technically localisable;
- more than one production region.

These exclusions protect delivery quality and avoid moderation, privacy, licensing, and operational complexity.

## 5. Outstanding UX specification

### Global principles

- Mobile is the primary consumption surface; desktop is the primary creation surface.
- The current event, day, and group must always be obvious.
- Never replace the timeline with a flat list on small screens. Allow horizontal panning, sensible stage-lane widths, and zoom presets.
- Selection must be optimistic and reversible. A failed sync shows a persistent retry state rather than silently reverting.
- Colour never carries meaning alone. Use labels, shapes, icons, and accessible text.
- Every interactive target is at least 44 by 44 CSS pixels.
- Respect safe areas, reduced motion, high contrast, keyboard navigation, and 200% text zoom.
- Target WCAG 2.2 AA.

### Information architecture

Unauthenticated navigation:

- Home/search
- Event preview
- Sign in/create account
- Pricing
- Help
- Privacy/terms

Authenticated event navigation uses four bottom tabs on mobile and a compact side/header navigation on desktop:

- `Schedule`
- `My route`
- `Crew`
- `Event`

Account, notification, installation, billing, and creator controls belong in a profile menu, not the event navigation.

### First-run journey

1. An invite URL contains an opaque group invitation token and event slug.
2. The landing screen shows event name, date, location, group name, and member avatars without disclosing the full membership list to an unauthenticated visitor.
3. Primary action: `Join the crew`. Secondary action: `Preview schedule`.
4. Account creation asks for email, display name, and password. Explain that only display name is visible to group members.
5. After verification/sign-in, redeem the invitation automatically and return to the schedule.
6. Point to the first performance with a one-sentence tip: tap once for Interested, again for Must see, again to clear. Also provide an accessible explicit menu.
7. After the user makes their first pick, explain crew badges.
8. After the third pick—or when the user opens reminders—offer PWA installation/notification setup. Never request browser notification permission on page load.

### Schedule timeline

- Sticky day selector and `Now` button.
- Time ruler remains visible during vertical scrolling.
- One lane per stage, with lane headers sticky during horizontal panning where supported.
- Performance height is proportional to duration but never less than the space needed for time, artist, selection state, and one attendee row. Use a compact layout below the normal content threshold rather than clipping.
- A `Now` line and a shaded past area appear during the event.
- Cards show start/end time, artist, stage (when lane context is hidden), selection priority, attendee count/avatars, changed/cancelled state, and clash marker.
- Selecting a card opens a bottom sheet on mobile and popover/dialog on desktop. It contains priority controls, full attendee names, conflicting picks, reminder state, and creator edit action when authorised.
- Zoom presets: `Compact`, `Comfortable`, `Detailed`; remember per device.
- Filter chips: stages, only crew picks, only my picks, changed, and search.

### My route

- Use the exact schedule time scale so the user never has to relearn the view.
- Show Must see cards at highest emphasis and Interested cards at lower emphasis.
- Retain overlapping alternatives behind/alongside the chosen card so clashes are visually explicit.
- Display the time between consecutive picks. If configurable stage travel time exceeds the gap, show `Tight transfer` with the missing minutes.
- Empty gaps may suggest an Interested act but must not automatically change a route.
- Provide a clear offline/sync indicator and last-updated time.

### Crew

Default view is `Consensus`:

- ranked Must see matches;
- group heatmap on the timeline;
- likely splits grouped by overlapping time;
- next reunion opportunity after a split;
- member filter with names and avatars, persisted per account/event/group;
- exact counts and names, never only ambiguous text such as “2 of 4”.

Do not algorithmically choose the group's route in version one. Present evidence and let people decide.

### Creator import workspace

Desktop: poster on the left, editable schedule on the right. Mobile: two tabs (`Poster`, `Review`) with row-to-image-position navigation where OCR coordinates exist.

The review table supports keyboard editing and includes day, start, end, stage, artist, validation state, and delete. Use virtualisation for large schedules. Allow bulk stage/day correction. Highlight low-confidence rows and overlaps. Provide undo/redo for the current import session.

The poster is represented only by an in-memory object URL. Revoke it when replaced, cancelled, published, or the page unloads. Never include its bytes in API calls, logs, analytics, crash reports, browser storage, or backups.

Publication flow:

1. `Review issues` focuses the first blocking issue.
2. `Publish N performances` presents a summary by day/stage and warns about replacements.
3. Server validates the complete draft again in one transaction.
4. On success, navigate to the published timeline and show a share action.

### Empty, error, and degraded states

Design them explicitly:

- no events found: explain how to create one;
- no group: personal mode remains fully useful and offers create/join actions;
- group has no other picks: share invitation action;
- offline with cache: show usable cached data and queued local changes;
- offline without cache: branded explanation with retry;
- OCR unsupported or failed: CSV/manual fallback, never a dead end;
- notification permission denied: steps to change browser/OS settings;
- schedule updated: non-blocking summary with `Review changes`;
- account/payment action failed: preserve entered data and offer retry.

## 6. Technical architecture

### Architecture choice

Replace the current single-file backend and 2,000-line browser script with a modular monolith. Do not introduce microservices. The current budget track uses one deployable ASP.NET Core application, the existing JSON volume, and a separately built TypeScript PWA served by the same application. PostgreSQL remains the implemented future relational adapter; a CDN and additional services are optional future infrastructure.

Target stack (paid infrastructure is deferred under the current budget track):

- .NET 10 ASP.NET Core Web API;
- PostgreSQL with EF Core and transactional migrations;
- ASP.NET Core Identity with email verification and cookie authentication;
- React + TypeScript + Vite for the PWA;
- TanStack Query for server state;
- React Router;
- IndexedDB through a small typed repository (Dexie is acceptable);
- Workbox-generated service worker plus an explicit Web Push handler;
- SignalR for schedule and group updates;
- Stripe Checkout, Billing Portal, and signed webhooks;
- a transactional email provider behind an application interface;
- OpenTelemetry-compatible structured logs and traces;
- xUnit, FluentAssertions, Testcontainers, and Playwright.

React is recommended because timeline interactions, offline state, OCR review, and a future Capacitor shell benefit from a typed component system and broad tooling. Do not rewrite business rules in React; the server remains authoritative.

### Solution structure

```text
RiffRoster.sln
src/
  RiffRoster.Domain/
    Accounts/
    Events/
    Groups/
    Scheduling/
    Billing/
    Notifications/
    Common/
  RiffRoster.Application/
    Abstractions/
    Accounts/
    Events/
    Groups/
    Selections/
    Billing/
    Notifications/
  RiffRoster.Infrastructure/
    Persistence/
    Identity/
    Billing/
    Email/
    Push/
    Observability/
  RiffRoster.Api/
    Endpoints/
    Auth/
    Middleware/
    Hubs/
  RiffRoster.Web/
    src/app/
    src/features/auth/
    src/features/events/
    src/features/schedule/
    src/features/route/
    src/features/groups/
    src/features/import/
    src/features/notifications/
    src/features/billing/
    src/components/
    src/data/
    src/styles/
tests/
  RiffRoster.Domain.Tests/
  RiffRoster.Application.Tests/
  RiffRoster.IntegrationTests/
  RiffRoster.ArchitectureTests/
  RiffRoster.E2E/
tools/
  RiffRoster.DataMigration/
docs/
```

Dependencies flow inward: `Api -> Infrastructure + Application -> Domain`. Domain must not reference EF Core, HTTP, Stripe, email, or filesystem types. Use feature folders and small endpoint modules, not a generic `Services` directory. Use commands/queries where they make behaviour clearer; do not add a mediator framework merely for ceremony.

### Core data model

All identifiers are UUID/ULID server-generated values. Display names and slugs are mutable; relationships never use them as keys.

| Entity | Essential fields and invariants |
|---|---|
| Account | Id, normalized email, display name, colour, status, created/updated; email unique; soft-lock supported |
| Session/Identity | Managed by Identity; security stamp enables global session revocation |
| Event | Id, ownerId, slug, name, location, timezone IANA ID, local start/end dates, visibility, status, source type, version, publishedAt |
| EventEditor | EventId, AccountId, role; unique pair |
| Stage | Id, EventId, name, order, colour, optional transfer zone; unique normalized name per event |
| Performance | Id, EventId, StageId, artist, startsAtUtc, endsAtUtc, status, revision; end after start; stable across edits |
| Selection | AccountId, PerformanceId, priority, updatedAt; unique pair |
| Group | Id, ownerId, name, createdAt, archivedAt |
| GroupMember | GroupId, AccountId, role, joinedAt; unique pair |
| GroupEvent | GroupId, EventId, createdAt; allows reusable crews across festivals |
| GroupInvite | Id, GroupId, tokenHash, optional EventId, expiresAt, maxUses, useCount, revokedAt |
| PushSubscription | Id, AccountId, endpointHash, encrypted endpoint/keys, userAgent summary, lastSeenAt, failure count, disabledAt |
| ReminderPreference | AccountId, EventId, leadMinutes, includeCrewNames, enabled |
| ReminderDelivery | AccountId, PerformanceId, subscriptionId, performanceRevision, sentAt, outcome; unique idempotency key |
| EventRevision | EventId, version, editorId, timestamp, change summary, compressed canonical schedule snapshot |
| Entitlement | AccountId or EventId, product, source, status, starts/endsAt, provider reference |
| PaymentEvent | Provider event ID, receivedAt, processedAt, result; provider ID unique for webhook idempotency |
| AuditEntry | actorId, action, target type/id, timestamp, safe structured metadata; append-only |

Store instants in UTC and store each event's IANA timezone separately. Convert to festival-local time at boundaries. Tests must cover daylight-saving transitions and performances after midnight that belong to the previous festival day.

### API conventions

- Version routes under `/api/v1`.
- Use problem-details responses with stable machine-readable error codes.
- Require optimistic concurrency (`If-Match`/version field) for event and performance edits.
- Mutation endpoints return the new resource/version.
- Paginate public event search; do not return the entire catalogue.
- Return one event schedule DTO optimised for the timeline rather than exposing EF entities.
- Keep private email, credentials, invite hashes, push endpoints, and billing references out of public DTOs.
- Generate and commit an OpenAPI document in CI; TypeScript API types/clients are generated from it.
- Protect cookie-authenticated mutations with antiforgery tokens and strict origin checks.
- Apply rate limits to authentication, invitation redemption, OCR publication, search, and test notifications.

Suggested endpoint groups:

```text
/api/v1/auth/*
/api/v1/me/*
/api/v1/events/*
/api/v1/events/{eventId}/schedule/*
/api/v1/events/{eventId}/selections/*
/api/v1/groups/*
/api/v1/invitations/*
/api/v1/push/*
/api/v1/billing/*
/api/v1/admin/*
/hubs/events
```

### Schedule update semantics

This is a critical invariant:

- Editing artist, stage, start, or end changes the existing Performance row and increments its revision. It never creates a replacement ID.
- Selections therefore remain attached automatically.
- A cancelled performance remains addressable and visible to users who selected it.
- Permanent deletion is creator/admin-only and removes dependent selections in one transaction after explicit confirmation.
- A replacement operation creates a new performance and migrates selections only when the editor explicitly requests it; record this in the audit log.
- Every published batch increments Event.version exactly once and emits one SignalR update after the transaction commits.

### Offline model

Cache the application shell using hashed assets. Store the last successfully loaded schedule, user's selections, relevant group summary, and server event version in IndexedDB per account/event. Do not cache authenticated API responses in the generic HTTP cache.

Offline selection changes enter an outbox with a client mutation ID. When online, submit idempotently. The server returns accepted state and event version. Resolve selection conflicts with last explicit user action; resolve schedule conflicts server-first and show removed/cancelled picks.

The UI must always distinguish:

- `Saved` — acknowledged by server;
- `Pending sync` — stored safely on device;
- `Sync failed` — needs user action.

### OCR boundary

Retain Tesseract in the browser and move it behind an `OcrEngine` interface. OCR output is an untrusted draft. Parsing and confidence scoring live in isolated TypeScript modules with fixture tests. Server publication receives only structured performance data.

Required safeguards:

- maximum decoded dimensions and input bytes;
- abort/cancel support;
- worker termination after completion;
- object URL revocation;
- no image analytics or error payloads;
- Content Security Policy permitting only self-hosted OCR assets;
- a clear user confirmation that they have permission to use the schedule information.

### Push notification design

Run reminders through a durable background job table in PostgreSQL rather than an in-memory timer. A single hosted worker may claim due jobs with `FOR UPDATE SKIP LOCKED`; this remains safe when scaled horizontally later.

Budget-track sequencing: implement notification UX, opt-in privacy, diagnostics and retry policy against the current single hosted worker first. Develop PostgreSQL job claiming locally. Do not deploy a separate worker or claim durable delivery until restart/idempotency tests pass for the production provider. A persistent single-process queue on the existing volume may be evaluated as a separate migration; do not silently substitute in-memory jobs for durability.

- Schedule jobs from UTC performance times and per-user lead settings.
- Changes to a performance invalidate/reschedule unsent jobs.
- Deliver once per performance revision/subscription using a unique key.
- Retry transient failures with bounded exponential backoff.
- Disable subscriptions on terminal 404/410 responses.
- Do not include group names or attendee names on a locked screen unless the user explicitly enables them.
- Provide a test notification and a diagnostic page showing permission, subscription, last server contact, and last delivery outcome.
- State plainly that delivery timing is controlled partly by the operating system.

### Payments

Use Stripe-hosted Checkout and Billing Portal on the web. Never handle card numbers.

Implement:

1. server-created Checkout Sessions for known product/price IDs;
2. success/cancel return pages that verify server-side entitlement state;
3. signature-verified webhooks;
4. idempotent storage of every webhook event before processing;
5. entitlement transitions for successful payment, renewal, cancellation, refund, and dispute;
6. billing portal link for customers;
7. admin reconciliation screen for webhook failures;
8. price/product configuration through environment variables, not source code.

Entitlements, not front-end flags, authorise paid actions. A failed renewal must not delete events; move them to read-only creator mode after a grace period.

## 7. Security, privacy, legal, and trust

The current shared global access code is unsuitable for a public service. Remove it after account migration. Use verified email accounts, secure cookie authentication, and standard ASP.NET Core Identity password hashing. Preserve display-name privacy: group members never see email or a private login identifier.

Required before public launch:

- verified email and password reset;
- optional passkey support may follow, but is not a launch blocker;
- session list and `sign out everywhere`;
- rate limiting and temporary lockout with non-enumerating auth errors;
- secure, HttpOnly, SameSite cookies; HSTS; restrictive CSP; no inline scripts;
- antiforgery protection for every authenticated mutation;
- encrypted push subscription secrets at rest;
- database backups plus a tested restore procedure;
- secret rotation procedure and separate staging/production secrets;
- dependency and container vulnerability scanning in CI;
- audit logs for permissions, billing, event publication, deletion, and admin actions;
- self-service account export and deletion;
- privacy policy, terms, acceptable-use/content policy, refund policy, and contact address;
- retention schedule for inactive accounts, invitations, audit entries, logs, and backups;
- consent-free essential cookies only at launch; do not add behavioural analytics until a compliant consent design exists where required;
- a documented takedown/ownership dispute process for event metadata and branding.

Festival schedules and account/display information still create legal responsibilities even when posters are not stored. Follow data minimisation, purpose limitation, storage limitation, confidentiality, and privacy by design. Obtain legal/accounting advice for the operator's Hungarian/EU business status, VAT, consumer rights, and terms before accepting payment; this plan is not legal advice.

If native apps are later published, both Apple and Google require an account-deletion route for apps that create accounts. Building it in the web version now avoids a later compliance rewrite.

## 8. Delivery plan for Astra

Each phase ends in a reviewable state that preserves a deployable existing application. New capabilities may remain disabled pending their production prerequisites. Astra must not combine phases into one large rewrite. At the start of every phase, read this document, inspect the current branch and tests, and write a short phase checklist in the task log. Preserve existing production data and functionality unless the phase explicitly migrates it.

### Budget-track execution order

1. Close Phase 1 implementation and local production-copy rehearsal; move hosted PostgreSQL cutover and operational services to the deferred infrastructure milestone below.
2. Implement Phase 1B JSON reliability before deploying further persisted-state changes: fail closed on corrupt/unsupported input, preserve the original source, verify atomic saves and recovery, and document single-replica operation. Add tests for failures without reseeding or losing acknowledged writes. This is the next implementation slice.
3. Proceed with Phase 3's TypeScript shell/timeline foundation using the current authenticated API. Preserve current account/group behavior. Keep public discovery and new identity screens gated until their server authorization exists.
4. Implement independent Phase 4/5/6 slices on the current provider where their transactional and restart guarantees can be tested. Keep schema changes small, versioned and reversible; extend migration/export comparison tooling whenever persisted fields change. Retest both adapters rather than leaving the future PostgreSQL path incompatible.
5. Phase 2 public Identity work can proceed locally with the PostgreSQL adapter and a development-only email sink. Production verification/reset requires a real deliverable email channel and a validated Identity persistence adapter. First assess already available email allowance and an embedded relational store on the existing volume if needed; neither is assumed safe, free or approved without validation. Do not build a bespoke JSON Identity implementation solely to avoid infrastructure costs. Keep the current private access gate until public identity is ready.
6. Phase 7 billing stays test-mode-only. Phase 8 help, redaction, security checks, local monitoring and recovery runbooks can proceed now. Paid launch and live billing remain gated on verified recovery, identity and operational readiness; no automatic Pro upgrade is required by this plan.

An incomplete deferred infrastructure milestone does not block these independent slices. Record implementation, local verification, deployment and launch status separately.

### Phase 0 — Baseline and safety net

Deliverables:

- Capture current API, data schema, seeded-event, OCR, push, group, and admin behaviour in characterisation tests.
- Add Playwright journeys for login, event switching, selecting an act, group creation/join, admin performance edit/delete, poster review, and notification setup mocks.
- Add automated JSON-store backup/restore verification.
- Record Lighthouse mobile scores and screenshots at 390x844, 768x1024, and 1440x900.
- Create an architecture decision record for PWA-first modular monolith.

Exit criteria:

- Tests pass against the unmodified behaviour.
- Production and groups JSON formats can be loaded by fixtures.
- No feature work starts without a reversible production-data migration path.

### Phase 1 — Modular backend and PostgreSQL migration readiness

Current status: implementation and local production-copy rehearsal complete. Hosted cutover is deferred by the user's budget instruction; it is not required to start Phase 1B or independent feature slices.

Deliverables:

- Create the solution/projects shown above.
- Move domain rules and endpoints out of `Program.cs` without changing public behaviour.
- Introduce EF Core/PostgreSQL schema and migrations.
- Implement a one-shot `RiffRoster.DataMigration` tool that imports JSON using stable generated mappings, validates counts/references, and can dry-run.
- Run dual comparison in an isolated local rehearsal: JSON source counts and canonical content hashes versus PostgreSQL. Repeat in the target environment when cutover is scheduled.
- Add automated daily database backup tooling and verify a restore drill locally. Defer its hosted worker deployment.

Deferred production migration procedure (execute only when infrastructure fits the existing budget or additional spending is authorized):

1. Take a named production volume backup.
2. Dry-run import and publish a count/orphan report.
3. Put production briefly into maintenance/read-only mode.
4. Run final import transaction.
5. Verify events, performances, users, selections, groups, memberships, subscriptions, and reminder deliveries.
6. Switch application connection to PostgreSQL.
7. Run smoke/E2E checks.
8. Retain the immutable JSON backup through at least two festival cycles.

Exit criteria:

- No count loss and no orphan foreign keys.
- Performance IDs remain stable.
- A reverse-export rollback is documented and rehearsed locally on a production copy. Previous deployed-image/hosted-environment rollback remains a cutover gate, not an implementation gate.
- Integration tests run against a real ephemeral PostgreSQL instance.

### Phase 2 — Public identity and account controls

Budget dependency: use the execution order above. Do not block frontend or independent group/import work on hosted PostgreSQL or email provisioning. Local identity tests do not authorize removing the production access code or enabling an email flow that cannot deliver messages.

Deliverables:

- ASP.NET Core Identity with verified email, display name, password reset, lockout, and session revocation.
- Migrate existing accounts through a forced `complete account` flow; do not expose their private login names.
- Remove the global member invitation code after migration; retain separate role-based admin authorisation.
- Account settings, export, and deletion.
- Invitation continuation through authentication.

Account deletion transaction:

- remove selections, memberships, invitations, push subscriptions, and pending reminders;
- transfer owned groups/events when another eligible owner exists, otherwise archive or delete based on explicit user choice;
- retain only financial records legally required, separated and minimally identified;
- create an audit record without copying deleted profile values.

Exit criteria:

- Auth responses do not reveal whether an email exists.
- Authorization tests cover horizontal access and every creator/admin endpoint.
- Deleted users cannot authenticate and do not appear in group UI or notifications.

### Phase 3 — TypeScript PWA and UX foundation

Start the compatible shell/timeline slice after Phase 1B, before Phase 2's deferred production rollout. New auth/public-preview flows depend on their server capabilities; existing authenticated journeys remain usable throughout migration.

Phase 3A local progress (8 September): strict TypeScript/Vite build, React day navigation, typed shared timeline geometry, keyboard/scrollable day tabs, and packaged/offline shell assets are implemented. The rest of Phase 3 remains open. Next slice: move schedule cards and their state/API boundary into typed components while retaining current authentication and stable performance IDs. This progress does not imply public identity, a generated API client, full offline schedules/outbox or production deployment.

Phase 3B local progress (9 September): the schedule grid, keyed performance cards and empty states now render in React. Pick updates retain keyboard focus; existing crew attendee, admin edit, conflict and filter behavior remains connected through the compatibility bridge. JSON request transport is typed and rejects malformed successful JSON while preserving status-specific errors. Validation: 11 frontend checks, 8 Chromium journeys, strict typecheck and .NET build passed; mobile/desktop screenshots reviewed. No Railway change or additional budget. Next slice: runtime validation and typed models for state/endpoint payloads, followed by further controller migration. Those payloads currently remain unknown at the transport boundary; this is not a completed typed state boundary or generated API client. Broader Phase 3 remains open.

Phase 3C local progress (9 September): public state snapshot models and runtime validation now guard initial load and group refresh before replacing client state. Validation covers required fields, real calendar dates, explicit-offset performance times, duplicate identities, selection references and group membership references. Unknown fields are discarded; stale preferred-event fallback and legacy timestamp precision are retained. All 14 frontend checks and 9 Chromium journeys passed, plus strict typecheck, .NET build and whitespace checks. No new Railway cost or production deployment. Next: typed mutation endpoint responses and remaining controller migration. Generated OpenAPI contracts, public identity and offline state/outbox remain open.

Phase 3D local progress (9 September): typed response methods now cover selection toggles, event/act changes, bulk import, group actions and preferred-event updates. Results are validated before local changes; returned identifiers must match the request where applicable, counters must be nonnegative integers, and malformed successful results advise refresh without automatic retry. Validation: 17 frontend checks and 10 Chromium journeys passed, plus strict typecheck, .NET build and whitespace checks. No Railway spending or production change. Next slice: session/push response validation, followed by remaining controller migration. Request bodies still originate in the legacy controller; generated OpenAPI contracts, public identity and offline outbox remain open.

Phase 3E local progress (9 September): session and notification responses now have typed validation, including public-key encoding, reminder preferences, explicit send acknowledgement and HTTP 204 cleanup. Existing frontend API call sites all use validated endpoint methods. This does not make request bodies or the legacy controller fully typed. Validation: 20 frontend checks and 10 Chromium journeys passed; strict typecheck, .NET build, release publish and published asset hash checks passed. No production change.

Next milestone — existing-service Railway deployment: pause further feature slices and prepare the current completed work for deployment to the existing single JSON service. Do not wait for PostgreSQL, paid staging, the complete React migration or public identity. Before deploying, review/package a reproducible source revision, run release regression checks, confirm production volume/configuration and rollback image, and take a fresh verified store/key backup with an off-volume copy. Then deploy to the existing service and check health, login, picks/reload, admin edits, groups and service-worker assets. Real notification delivery needs a controlled device check; browser mocks do not prove delivery. Record the deployed revision and outcome. No fixed date is assigned until live readiness checks pass. No resource increase or paid service is authorized.





Deliverables:

- Establish React/TypeScript feature structure and generated API client.
- Create design tokens for colour, spacing, typography, elevation, focus, motion, and stage palettes.
- Build accessible shell, auth, event search/preview, profile, and event navigation.
- Rebuild schedule and My route timeline with mobile gestures and keyboard equivalents.
- Implement IndexedDB cache/outbox and explicit sync states.
- Preserve the service worker, manifest, install flow, and notification deep links.

Exit criteria:

- No uncaught TypeScript errors; strict mode enabled with no routine `any`.
- WCAG 2.2 AA automated checks pass and keyboard journeys are manually verified.
- Lighthouse targets on a representative production build: Performance >= 90, Accessibility >= 95, Best Practices >= 95, PWA installable.
- Initial JS is code-split; OCR libraries load only when import is opened.
- Cached event opens in airplane-mode E2E test.

### Phase 4 — Groups, priorities, and live schedule collaboration

Deliverables:

- Reusable groups, roles, event association, invite deep links, expiry/revocation, and owner transfer.
- Three-state selections and migration of existing picks to `Interested` (or a documented chosen default).
- Consensus, split, and reunion views.
- Persist comparison member filters server-side so they follow the account across devices.
- SignalR invalidation for schedule, selection, and group changes with polling fallback.
- `.ics` personal route export.

Exit criteria:

- Two simulated users see selections and schedule changes without refresh.
- Group data is visible only to members.
- Invite tokens are stored hashed, can expire/revoke, and cannot be enumerated.
- Member names/badges never clip at minimum card height or 200% text zoom.

### Phase 5 — Creator workflow and catalogue

Deliverables:

- Event drafts, visibility, ownership/editors, slugs, search, and duplicate reporting.
- Isolated client OCR pipeline and side-by-side review workspace.
- CSV template/import.
- Atomic publish, validation, event versions, history, rollback, cancellation, replacement, and stable-ID editing.
- Admin moderation, merge, transfer, and audit tools.

Exit criteria:

- Poster bytes never appear in a captured network request, IndexedDB/localStorage, logs, analytics, or server filesystem test.
- A 500-performance draft can be edited responsively on a mid-range mobile profile and desktop.
- Updating an existing performance retains all selections and reschedules reminders.
- Duplicate merge is covered by a transactional integration test and dry-run preview.

### Phase 6 — Durable reminders

Deliverables:

- PostgreSQL reminder job/delivery design, retry and dead-letter visibility.
- Per-event preferences and privacy-safe payloads.
- contextual Android/iOS/Desktop setup guidance and diagnostics.
- expiry handling for dead subscriptions.
- operational metrics: due, sent, failed transient, disabled terminal, latency percentiles.

Exit criteria:

- Lock/restart the server during a due window; jobs still deliver once after recovery.
- Performance edits invalidate old scheduled jobs.
- Automated tests cover VAPID headers and Web Push payload compatibility.
- UI never promises exact delivery time.

### Phase 7 — Billing and entitlements

Deliverables:

- Stripe Checkout/Portal and webhook ingestion.
- Creator annual entitlement and event-level Verified entitlement.
- One-free-event allowance.
- Pricing, checkout result, billing settings, invoices/portal link, and grace/read-only states.
- Manual admin grant for organiser pilots.
- Refund and dispute operational runbooks.

Exit criteria:

- Stripe test-mode scenarios pass for payment, duplicate webhook, renewal, cancellation, failed renewal, refund, and dispute.
- Paid authorization is enforced server-side.
- Payment success is never inferred only from a redirect URL.
- Existing content remains readable after entitlement expiry.

### Phase 8 — Launch hardening

Budget sequencing: implement code, documentation and local checks now. Reuse the existing service for health checks, redacted logs and hosted background work where capacity permits. Custom domains, paid monitoring/email, hosted staging, separate workers and other new charges are deferred unless already covered by existing resources. Record unresolved release gates explicitly.

Deliverables:

- Production-grade configuration, custom domain, transactional email domain authentication, staging environment, and feature flags.
- CI pipeline: format, build, unit, architecture, integration, front-end test, E2E smoke, dependency audit, container scan, migration validation.
- Structured logging, traces, error alerting, uptime check, database/push/job dashboards, and redaction tests.
- Privacy/terms/refund/acceptable-use pages and self-service data controls.
- Help centre pages for install, notifications, OCR correction, groups, offline use, and account recovery.
- Support inbox and incident/rollback runbooks.
- Seed public catalogue with 10 permission-appropriate/current community schedules.

Exit criteria:

- Restore the latest backup into staging and pass smoke tests.
- A deployment with a failed migration automatically stops before serving traffic.
- No secrets or personal data appear in logs or front-end bundles.
- Critical user journeys pass on current Chrome Android, Safari iPhone, Safari macOS, Chrome desktop, and Firefox desktop.
- Five external groups complete a real schedule without developer assistance.

## 9. Testing requirements

### Domain/unit tests

Cover schedule-day assignment after midnight, timezone/DST conversion, overlap detection, priority transitions, group ownership transfer, invite lifecycle, entitlement policy, performance edit identity, cancellation/replacement, and reminder idempotency.

### Integration tests

Use real local PostgreSQL through Testcontainers or the isolated portable test server for its adapter; no hosted database is required for tests. Also test the active JSON provider's atomic saves, validation, backup/restore and every feature deployed against it. Test transactions, concurrency conflicts, database constraints, migrations from every supported schema version, Identity flows, Stripe webhook signatures/idempotency, push job claiming, and account/event deletion cascades as those capabilities are implemented.

### Front-end tests

Test pure timeline geometry and OCR parsers with fixtures. Component-test selection states, badges, dialogs, offline banners, import validation, and responsive navigation. Avoid brittle snapshots as the primary assertion.

### E2E journeys

At minimum:

1. public preview -> account -> invite redemption -> first pick;
2. personal planner without group;
3. create group -> share -> second user joins -> compare;
4. overlapping Must see picks -> visible clash -> route change;
5. creator OCR fixture -> correct rows -> publish -> attendee sees schedule;
6. edit performance time -> existing picks retained -> clients update;
7. offline open -> queue pick -> reconnect -> sync;
8. enable/test/disable push with browser APIs mocked plus a separate real-device checklist;
9. Creator checkout in Stripe test mode;
10. export and delete account;
11. admin unpublish/restore/merge event;
12. screen-reader and keyboard-only core journey.

### Performance budgets

- API p95 under 300 ms for schedule reads and selection writes at expected launch load.
- Timeline interaction remains at 55+ FPS on a representative mid-range Android device.
- Initial compressed JS under 250 KB where feasible, excluding lazy chunks.
- OCR, admin, and billing code never load on the attendee schedule path unless used.
- Public schedule remains usable on a throttled 3G profile after first content appears.

Run a modest load test before each large festival: 500 concurrent schedule readers and 50 selection writes/second is an appropriate first target, then revise from observed demand.

## 10. Deployment and operations

### Current operating mode

Keep the existing Railway plan, application service, `/data` volume and one replica. Do not create PostgreSQL, a backup service, hosted staging, extra volumes or additional replicas now. Serve frontend assets from the existing application and run compatible maintenance/reminder work in that process. No fixed subscription upgrade is a prerequisite for implementation or payments; actual release readiness must be demonstrated separately.

Retain the existing daily JSON snapshots. They share the application volume and are not sufficient by themselves for volume-loss recovery. Keep verified off-volume JSON/key copies using existing local storage; until a no-cost automated transfer has been implemented and tested, record this as an operator-run procedure with an owner and last verified date. Do not describe manual copies as automated or guarantee recovery when freshness/retention is unverified. The 8 September preflight copy is evidence of one successful backup, not a standing backup service.

Run rehearsal servers locally with separate working copies, keys and test credentials. Disable real email, billing and push delivery in rehearsals. Before any production change, take a fresh backup, document migration/rollback and run smoke checks on the actual deployed build. A local test pass must not be presented as a completed deployment.

### Deferred infrastructure milestone

Keep the PostgreSQL adapter, import/export tools, daily dump worker and restore drill ready. When a cutover is justified and funded, provision isolated production/rehearsal storage, configure protected credentials and off-volume retention, apply migrations before serving traffic, compare the final maintenance-window source and verify the deployed application. Keep destructive database rollback out of the deployment path. Review then-current provider prices before requesting any budget.

### When to raise the budget gate

Measure before escalating. Examples are repeated latency/error failures at the agreed pilot load after reasonable optimization; volume/usage limits that cannot be met through approved retention; recovery requirements that existing local backup arrangements cannot satisfy; or a required identity/durable-write capability with no validated implementation on existing resources. The load targets in section 9 are measurements and release gates, not permission to buy capacity.

For a real blocker, provide observed evidence, the user-facing capability affected, no-additional-cost alternatives and why they are insufficient, and the smallest proposed resource change with a current cost estimate. Ask only at that point. Continue all unaffected implementation. Do not silently exceed the budget or substitute an unapproved paid provider.

## 11. Analytics and validation

Instrument only product events needed for decisions, using opaque account/event IDs and no artist taste profile in third-party analytics.

North-star metric:

> Weekly active planned groups: groups in which at least two members made or viewed picks for the same upcoming event during the week.

Activation funnel:

- invite/event landing viewed;
- schedule previewed;
- account completed;
- group joined;
- first pick;
- third pick;
- return visit;
- PWA installed;
- reminder test successful.

Creator funnel:

- create started;
- image/CSV/manual method chosen;
- OCR completed;
- validation issues resolved;
- draft published;
- invitation shared;
- second active group member;
- second event attempted;
- checkout started/completed.

Launch targets to validate, not manipulate:

- >= 65% of authenticated invite visitors join the intended group;
- >= 70% of joined members make at least three picks;
- >= 40% of activated groups return in the seven days before their festival;
- >= 25% of creators publish a valid event after starting import;
- >= 8% of creators who attempt a second event buy Creator;
- >= 99.5% reminder job success excluding invalid/revoked endpoints;
- support volume below 5 tickets per 100 activated groups.

Run 15 problem interviews before billing implementation: five group organisers, five ordinary group members, and five festival organisers. Ask about their latest behaviour and budget, not whether they like the idea. Secure one paid organiser pilot before building advanced analytics.

## 12. Release strategy

### Private alpha

- 5 groups, 2–3 events, free;
- watch users perform invitation, selection, comparison, install, and offline tasks;
- fix every blocker and recurring confusion.

### Public beta

- searchable catalogue and one free creator event;
- no payment required yet, but show planned Creator features and collect intent;
- seed 10 high-quality events and recruit community maintainers;
- run through one live festival weekend with on-call monitoring.

### Paid launch

- enable Creator annual checkout;
- onboard the first paid Verified Festival manually;
- publish transparent pricing, data practices, status page, and support expectations;
- post examples showing poster -> corrected schedule -> crew consensus, not generic feature lists.

### Growth loops

- invitation links with a high-quality event preview;
- tasteful `Schedule maintained with RiffRoster` attribution and creator profile;
- QR asset generator for organisers and community maintainers;
- public event pages that are useful and indexable without login;
- share cards that show event/date/group count without exposing private picks;
- post-festival prompt for the creator to duplicate their workflow for the next event.

Avoid referral credits until baseline conversion is known. The product should spread because every additional member improves the crew plan.

## 13. Astra implementation rules

GPT-6 Astra Medium should follow these rules on every implementation task:

1. Read the applicable phase and inspect current code/tests before proposing edits.
2. Work on one phase or one vertical slice at a time. Do not perform an unreviewable whole-system rewrite.
3. State data migrations and rollback before changing persisted structures.
4. Preserve unrelated user changes and never replace production JSON/database content with seed data.
5. Use stable entity IDs. Never use artist, display name, email, group name, or event slug as a foreign key.
6. Keep domain policy out of endpoints and UI components.
7. Add or update tests in the same change as behaviour.
8. Prefer explicit typed contracts; reject invalid input at boundaries and enforce invariants in the domain/database too.
9. Treat OCR, browser storage, Web Push payloads, Stripe webhooks, and invitation tokens as untrusted input.
10. Do not log secrets, poster content, email addresses, push endpoints, password/reset tokens, invitation tokens, or raw payment payloads.
11. Keep destructive actions behind a confirmation that names the target and explains selection/data impact.
12. Run formatting, build, tests, migration checks, and relevant Playwright journeys before declaring completion.
13. Report exact files changed, migrations added, tests run, known limitations, and deployment/data impact.
14. Ask for direction if a necessary decision changes pricing, privacy promises, or irreversible production data; continue independent work. Infrastructure spending follows the evidence-based budget gate in section 1, not an automatic phase dependency.

Definition of done for any feature:

- behaviour and non-goals documented;
- authorization and validation enforced server-side;
- accessible responsive states implemented;
- loading, empty, offline, error, and success states handled;
- telemetry is privacy-safe;
- unit/integration/E2E coverage proportional to risk;
- no regressions in existing core journeys;
- migration and rollback documented when data changes;
- isolated local verification complete for implementation; target-environment smoke and rollback verification complete before production deployment. Paid hosted staging is deferred, and local results must never be labelled hosted staging results.

## 14. Sources and current assumptions

Current platform and commercial facts should be rechecked at the start of implementation because policies and prices change:

- Apple documents standards-based Web Push for iOS 16.4+ Home Screen web apps: https://developer.apple.com/documentation/usernotifications/sending-web-push-notifications-in-web-apps-and-browsers
- Apple App Review requires native apps to offer more than a repackaged website: https://developer.apple.com/app-store/review/guidelines/
- Apple Developer Program membership is currently USD 99/year; its Small Business Program advertises a 15% commission for qualifying developers: https://developer.apple.com/programs/whats-included/ and https://developer.apple.com/app-store/small-business-program/
- Google Play requires a stable, responsive, meaningful application and restricts low-value WebView wrappers: https://support.google.com/googleplay/android-developer/answer/9898783 and https://support.google.com/googleplay/android-developer/answer/9899034
- Google Play's fee rules vary by programme and region and are changing in 2026; consult the live schedule rather than encoding assumptions: https://support.google.com/googleplay/android-developer/answer/112622
- Apple and Google account-deletion requirements: https://developer.apple.com/support/offering-account-deletion-in-your-app and https://support.google.com/googleplay/android-developer/answer/13327111
- European Commission GDPR principles, including minimisation and privacy by design: https://commission.europa.eu/law/law-topic/data-protection/information-business-and-organisations/principles-gdpr_en
- Stripe Hungary standard pricing: https://stripe.com/en-hu/pricing
- Railway plans and resource pricing: https://docs.railway.com/pricing/plans and https://docs.railway.com/pricing
- Existing/adjacent product references used to test differentiation: https://clashfinder.com/, https://festivalsplits.com/, https://setlineapp.com/organisers, and https://www.festiview.com/

## 15. Final launch checklist

These are release gates, not prerequisites for continuing local implementation. Do not call the product confidently publishable until every applicable item is true. Billing-specific items apply to paid launch; infrastructure choices must satisfy the same tested guarantees within the approved budget.

- [ ] Five external groups complete the core journey unassisted.
- [ ] One full live-festival weekend has passed with no data loss.
- [ ] The selected production provider's migration, backup and restore drill pass from production-like data. PostgreSQL cutover is required only if PostgreSQL is selected for deployment.
- [ ] Public identity, verification, reset, export, deletion, and session revocation work.
- [ ] Public/private/unlisted event access is authorization-tested.
- [ ] Stable performance edits preserve selections and reminders.
- [ ] Offline schedule and queued picks work on Android and iOS.
- [ ] Push setup, diagnostics, privacy controls, and operational dashboard work.
- [ ] OCR image bytes are proven not to leave/persist on the device.
- [ ] Stripe test matrix and entitlement authorization pass.
- [ ] Pricing, help, privacy, terms, refunds, and contact pages are live.
- [ ] Accessibility, performance, security, load, and cross-browser gates pass.
- [ ] Rehearsal and production data/secrets are isolated and rollback is rehearsed; hosted staging is deferred unless required by a demonstrated release need.
- [ ] Monitoring, backups, restore, incident, and support procedures have owners.
- [ ] A paid organiser pilot or equivalent strong willingness-to-pay evidence exists.
