# Private-beta characterization contract

Phase 0 records the behaviour below to protect migration. Desired public-version behaviour is specified separately in FIRST_PUBLIC_VERSION_PLAN.md.

## API and identity

`GET /api/health` and the static shell are anonymous. `GET /api/state` requires a session. `POST /api/session` signs in or creates/claims an account using private login name, display name, password and a shared member/admin code. Cookie flags include HttpOnly and SameSite Strict; production additionally requires Secure. Display name is currently the stable selection/group identity. State exposes only public profile fields and co-members' selections. `POST /api/session/logout` ends the cookie session.

`POST /api/selections/toggle` derives identity from the session and toggles a two-state pick. Unknown acts return 404. `PUT /api/account/last-event` persists a known event for that account. There is no public preview, verified email, three-state priority, versioned API, antiforgery scheme or account deletion flow yet.

## Groups

`POST /api/groups`, `POST /api/groups/join`, `POST /api/groups/{id}/leave`, and owner-only `POST /api/groups/{id}/join-code` manage private group views. Codes are stored hashed. Rotation invalidates the prior code. A leaving owner transfers to the earliest remaining member; an empty group is removed. Leaving preserves personal picks and hides former members unless another group still connects them. Public DTOs omit login credentials and join-code hashes.

## Events, edits and import

Every event/act mutation is admin-only: create/delete event, add/edit/delete act and bulk import. Bulk input is limited to 1–500 rows and endpoint validation rejects invalid fields/durations. The store checks all rows' schedule days before adding anything, and skips exact normalized artist/stage/time duplicates, including within the same batch.

Acts before 06:00 belong to the previous schedule day. Time editing can move an act between days while retaining its ID and picks. Editing removes prior reminder delivery records. Deleting an act removes its picks and delivery records; event deletion removes its schedule. The browser asks for confirmation naming the act and pick impact.

The seed has 164 positive-duration acts, five days and stable `ba26-*` identifiers, including Tuesday warm-up and KAL slots. The seed initially reports data version 6; the existing loader upgrades to current version 7 on reopening. This behaviour is unchanged.

## OCR, timeline and push

The browser retains a poster preview as a blob URL, runs Tesseract locally, exposes parsed rows for correction and publishes only structured performance JSON. The test injects synthetic recognition text, exercises real image preparation/review/publication, checks preview revocation and worker termination, and observes outgoing mutations. It does not establish every future privacy gate or benchmark real recognition.

Timeline geometry uses 2.2 pixels/minute and a 32px minimum card height. Clash helpers return pairs of overlapping performances; exactly adjacent performances do not clash. OCR fixtures cover digit correction, missing end times, duplicates and midnight rollover.

Push routes expose configuration availability, subscription storage/removal and owned-device test delivery. Reminders become due at the chosen lead and stop being due at performance start. Delivery history deduplicates a device/performance/start-time across restarts. Editing or deselecting invalidates history. Crew candidates include only people sharing a group; payload inclusion is controlled by device preference. Browser permission is requested through the enable action. No real push request is made by the suite.

## Persistence and recovery

Synthetic v5 (pre-group/account-claim fields) and v7 (all collections including groups/push/deliveries) fixtures load through the actual DataStore. Backup tests verify once-daily immutable bytes, 14-file retention and restore into a separate host without reseeding. The standalone verifier checks unique keys and foreign references before copying to a new isolated destination. It refuses overwrites and unsupported versions.

Phase 0 originally characterized the dangerous malformed-file reseeding fallback. Phase 1B deliberately replaces that behavior with startup refusal and original-byte preservation, with regression tests for failed writes and recovery. Consult JSON_RECOVERY_RUNBOOK.md before testing any real backup. No source/production JSON migration was performed by Phase 0.
