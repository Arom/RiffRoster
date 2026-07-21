# RiffRoster

RiffRoster is a lightweight group clashfinder built with ASP.NET Core and plain browser JavaScript. Friends use individual accounts plus a shared site access code, star the acts they want to see, and compare routes inside groups they create or join.

## Run locally

You need the .NET 10 SDK.

```powershell
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

Do not scale the service beyond one replica while using the JSON store. If the audience grows, replace `DataStore` with PostgreSQL before scaling horizontally.

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
- JSON persistence with automatic daily snapshots

## Project shape

- `Program.cs` — authentication, HTTP endpoints, data store, and backup worker
- `PushNotifications.cs` — Web Push subscriptions, reminder scheduling, delivery, and deduplication
- `SeedData.cs` — starter running order with an empty crew
- `wwwroot/` — responsive browser interface
- `Dockerfile` — production .NET 10 container
- `railway.json` — Railway build and health-check settings
- `App_Data/store.json` — generated local runtime data, ignored by git

## OCR components

Poster recognition uses self-hosted Tesseract.js 7, Tesseract.js Core 7, and the English Tesseract language data. The browser downloads these static components from RiffRoster itself and performs recognition locally. Third-party license texts are included under `wwwroot/vendor/tesseract/licenses/`.
