# RiffRoster

RiffRoster is a lightweight group clashfinder built with ASP.NET Core and plain browser JavaScript. Friends use individual accounts plus a shared site access code, star the acts they want to see, and compare routes inside groups they create or join.

## Run locally

You need the .NET 10 SDK, Node 24 and pnpm 11.19. Frontend build tools run locally; the deployed app still needs only .NET.

```powershell
pnpm install --frozen-lockfile
pnpm build:web
dotnet run
```

Open `http://localhost:5178`. The development-only codes are:

- Member: `friends-local`
- Administrator: `admin-local-only`

The administrator code reveals schedule editing controls. Neither development code is used by the production Docker image.

## Deploy on Railway

The repository includes a production `Dockerfile` and `railway.json` health-check configuration.

1. Push this project to a GitHub repository.
2. In Railway, create a project and choose **Deploy from GitHub repo**.
3. Add a volume to the RiffRoster service and mount it at `/data`.
4. Add the variables below to the service.
5. Keep the service at one replica while it uses JSON file storage.
6. In the service’s networking settings, generate a Railway domain.

Required production variables:

```text
ASPNETCORE_ENVIRONMENT=Production
RIFFROSTER_INVITE_CODE=<a random code of at least 16 characters>
RIFFROSTER_ADMIN_CODE=<a different random code of at least 16 characters>
RIFFROSTER_DATA_PATH=/data/store.json
RIFFROSTER_BACKUP_DIR=/data/backups
RIFFROSTER_KEYS_PATH=/data/keys
RIFFROSTER_VAPID_PUBLIC_KEY=<generated public Web Push key>
RIFFROSTER_VAPID_PRIVATE_KEY=<generated private Web Push key>
RIFFROSTER_VAPID_SUBJECT=https://your-riffroster-domain.example/
```

Use a password manager to generate both codes. Share only `RIFFROSTER_INVITE_CODE` with friends. Each person also chooses a private login name and password; only their separate display name appears to other users. Anyone using `RIFFROSTER_ADMIN_CODE` can add running orders and performances. Changing either code invalidates existing sessions.

Generate the Web Push keys once, then copy the two values into Railway without committing them:

```powershell
dotnet run -- --generate-vapid
```

Keep the same VAPID keys across deployments so existing browser subscriptions continue to work. `RIFFROSTER_VAPID_SUBJECT` must be an HTTPS URL or `mailto:` address identifying the operator.

Railway should detect the Dockerfile automatically. Its deployment health check calls `/api/health`. The app listens on port `8080`, which Railway discovers and exposes through HTTPS.

### Persistence and backups

Everything under `/data` lives on the Railway volume:

- `store.json` contains account hashes, groups, users, events, selections, performances, browser push subscriptions, and reminder delivery records.
- `backups/` retains up to 14 daily JSON snapshots.
- `keys/` keeps session-encryption keys across deployments.

The app creates a backup check every six hours and at most one snapshot per UTC day. Also enable Railway volume backups when they are available on your plan; the in-app snapshots and infrastructure backups protect against different failure modes.

Do not scale the service beyond one replica while using the JSON store. PostgreSQL is available through `RIFFROSTER_STORAGE=Postgres` and a protected `RIFFROSTER_POSTGRES_CONNECTION`, after explicit migration and import. Follow `docs/PHASE_1_MIGRATION.md` for cutover, daily PostgreSQL backup worker setup and rollback. Keep one reminder worker until durable delivery jobs are implemented. PostgreSQL is opt-in; an ordinary deployment retains JSON.

## Security model

- Passwords are salted and hashed with PBKDF2-SHA256; plaintext passwords are never stored.
- Login and group-code attempts are rate limited per client address.
- Sessions use signed, HttpOnly, SameSite cookies.
- API requests derive the display identity from the signed session, so clients cannot select bands as another user.
- Private login names and password hashes are never included in browser-facing state.
- Only administrator sessions can create running orders or add performances.
- Production refuses to start without two different access codes of at least 16 characters.
- The site is still intended for a small private audience; it does not yet include email recovery, multifactor authentication, or an external identity provider.

## Included features

- 164 seeded Brutal Assault 2026 performances, including Tuesday warm-up and every KAL slot
- Multiple running orders and administrator editing
- Browser-only poster OCR with a side-by-side image and editable import review; poster files are never uploaded or stored
- Per-user band picks and visible friend attendance
- Personal mode for users who are not in a group, plus multi-group creation, join codes, switching, and leaving
- Time-scaled Running order and My route views that make overlaps visible across stage lanes
- Crew comparison with full-group matches, shared picks, and solo detours
- Schedule search and stage filters
- Responsive desktop and mobile layouts
- Installable PWA with opt-in per-device concert reminders and optional crew names
- JSON persistence with automatic daily snapshots, or PostgreSQL with validated import and a separate backup worker

## Project shape

- `Program.cs` — compatibility entry point to the API host
- `src/RiffRoster.Domain/` — domain records, credentials and schedule rules
- `src/RiffRoster.Application/` — account, group, selection and schedule operations; persistence ports
- `src/RiffRoster.Infrastructure/` — JSON/PostgreSQL adapters, EF migrations, push and backups, seed schedule
- `src/RiffRoster.Api/` — HTTP composition and endpoint modules
- `src/RiffRoster.Web/` — strict TypeScript, React day navigation and shared timeline geometry
- `wwwroot/generated/` — generated frontend bundle; build before .NET, not committed
- `tools/RiffRoster.DataMigration/` — dry-run, import, comparison, export, backup and restore CLI
- `tests/` — characterization, domain, application, architecture, PostgreSQL integration and browser checks
- `wwwroot/` — responsive browser interface
- `Dockerfile` — production .NET 10 container
- `railway.json` — Railway build and health-check settings
- `App_Data/store.json` — generated local runtime data, ignored by git

## First public version implementation

The specification is in `docs/FIRST_PUBLIC_VERSION_PLAN.md`. Implementation proceeds one phase at a time; Phase 0 establishes the private-beta baseline, not public-launch readiness. Progress and limitations are in `docs/IMPLEMENTATION_LOG.md`, and restore instructions are in `docs/JSON_RECOVERY_RUNBOOK.md`.

The 8 September budget revision keeps the existing Railway JSON service, volume and one replica. Additional paid infrastructure and PostgreSQL cutover are deferred. Phase 1 implementation/local rehearsal, Phase 1B JSON reliability and Phase 3A's initial TypeScript/React slice are complete locally. Invalid existing JSON now stops startup, supported upgrades preserve their source, and failed saves do not publish in-memory changes. These changes have not been deployed to Production. Local development and independent features continue without a budget increase. Public release checks still apply, and verified off-volume backups currently require an operator.

### Run the safety net

Use .NET 10, Node 24, pnpm 11.19 and Python 3.12 or newer:

```text
pnpm install --frozen-lockfile
pnpm build:web
dotnet restore RiffRoster.sln
dotnet build RiffRoster.sln --no-restore
dotnet test RiffRoster.sln --no-build
dotnet format whitespace RiffRoster.sln --no-restore --verify-no-changes
python -m unittest discover -s tests/tools -v
pnpm test
pnpm exec playwright install chromium
pnpm test:e2e
pnpm baseline
```

PostgreSQL integration tests use Testcontainers with Docker by default. Install PostgreSQL 17 client tools for backup tests and set `RIFFROSTER_PG_BIN` if they are not on PATH. Without Docker, set `RIFFROSTER_TEST_POSTGRES` to an explicitly disposable local PostgreSQL server whose test role can create/drop databases. Tests create unique databases and remove those databases. Never use a production connection.

On Linux, use `pnpm exec playwright install --with-deps chromium`. Browser runs start a fresh isolated app on port 5189, refuse an existing server, and use only synthetic accounts. Run `pnpm test:e2e` once normally for JSON, then with `RIFFROSTER_E2E_PROVIDER=Postgres` for PostgreSQL (same Docker/local test-server setup). Do not point tests at production. OCR recognition and Web Push are mocked in browser journeys; parser fixtures, real HTTP endpoints and both storage providers are tested separately. Real OCR accuracy and physical-device notification delivery remain manual checks.

`pnpm baseline` captures the three requested schedule viewports and a mobile Lighthouse report under `docs/baseline/`. It uses a synthetic authenticated account, a disposable browser profile and port 9237 for local Chrome debugging. Run it after browser tests have stopped. Scores are measurements, not release gates; native installation and offline/push behaviour require separate device checks.

The GitHub Actions baseline workflow builds and runs the suite. Test tooling, fixtures, reports and dependencies are excluded from the production build/container context.

### Frontend migration

Phase 3A adds a small React component inside the existing day-tab container and extracts the shared Schedule/My route geometry to TypeScript. Day tabs support Left/Right, Home/End and a single tab stop. Existing login, groups, picks, imports and notification flows still use the current API and browser controller. The new code is strictly typed; the legacy controller is not yet TypeScript. Public identity, generated API contracts, the complete React shell and offline schedule/outbox remain later slices.

Run `pnpm build:web` after frontend edits; `pnpm typecheck` checks types without writing assets. Vite outputs a production IIFE bridge, and the build fingerprints shell assets plus service-worker logic for cache invalidation. The new bundle is precached; this does not add offline authenticated schedules. Both `pnpm test` and `pnpm test:e2e` rebuild frontend assets, and E2E also rebuilds the web host. A clean .NET build fails with instructions if the bundle is missing. Docker and CI build it automatically before .NET publication, with no additional hosted service.

Build references: [Vite production builds](https://vite.dev/guide/build), [Vite TypeScript type-checking behavior](https://vite.dev/guide/features).

## OCR components

Poster recognition uses self-hosted Tesseract.js 7, Tesseract.js Core 7, and the English Tesseract language data. The browser downloads these static components from RiffRoster itself and performs recognition locally. Third-party license texts are included under `wwwroot/vendor/tesseract/licenses/`.
