# RiffRoster

RiffRoster is a lightweight group clashfinder built with ASP.NET Core and plain browser JavaScript. Friends sign in with a shared access code, star the acts they want to see, and compare their routes.

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
```

Use a password manager to generate both codes. Share only `RIFFROSTER_INVITE_CODE` with friends. Anyone using `RIFFROSTER_ADMIN_CODE` can add running orders and performances. Changing either code invalidates existing sessions.

Railway should detect the Dockerfile automatically. Its deployment health check calls `/api/health`. The app listens on port `8080`, which Railway discovers and exposes through HTTPS.

### Persistence and backups

Everything under `/data` lives on the Railway volume:

- `store.json` contains users, events, selections, and performances.
- `backups/` retains up to 14 daily JSON snapshots.
- `keys/` keeps session-encryption keys across deployments.

The app creates a backup check every six hours and at most one snapshot per UTC day. Also enable Railway volume backups when they are available on your plan; the in-app snapshots and infrastructure backups protect against different failure modes.

Do not scale the service beyond one replica while using the JSON store. If the audience grows, replace `DataStore` with PostgreSQL before scaling horizontally.

## Security model

- Sessions use signed, HttpOnly, SameSite cookies.
- API requests derive the username from the signed session, so clients cannot select bands as another user.
- Only administrator sessions can create running orders or add performances.
- Production refuses to start without two different access codes of at least 16 characters.
- The site is still intended for a small private group; access codes are shared secrets, not individual accounts.

## Included features

- 129 seeded Brutal Assault 2026 performances
- Multiple running orders and administrator editing
- Per-user band picks and visible friend attendance
- A focused “My route” view with automatic time-clash warnings
- Crew comparison with full-group matches, shared picks, and solo detours
- Schedule search and stage filters
- Responsive desktop and mobile layouts
- JSON persistence with automatic daily snapshots

## Project shape

- `Program.cs` — authentication, HTTP endpoints, data store, and backup worker
- `SeedData.cs` — starter running order and demo crew picks
- `wwwroot/` — responsive browser interface
- `Dockerfile` — production .NET 10 container
- `railway.json` — Railway build and health-check settings
- `App_Data/store.json` — generated local runtime data, ignored by git
