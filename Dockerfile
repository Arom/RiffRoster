FROM node:24-bookworm-slim AS frontend
WORKDIR /src
RUN npm install --global pnpm@11.19.0
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY src/RiffRoster.Web/ src/RiffRoster.Web/
COPY wwwroot/ wwwroot/
COPY tools/stamp-web-cache.mjs tools/stamp-web-cache.mjs
RUN pnpm build:web

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY RiffRoster.csproj NuGet.Config ./
COPY src/ src/
COPY tools/RiffRoster.DataMigration/ tools/RiffRoster.DataMigration/
RUN dotnet restore RiffRoster.csproj --configfile NuGet.Config
RUN dotnet restore tools/RiffRoster.DataMigration/RiffRoster.DataMigration.csproj --configfile NuGet.Config

COPY . .
COPY --from=frontend /src/wwwroot/generated/ wwwroot/generated/
COPY --from=frontend /src/wwwroot/service-worker.js wwwroot/service-worker.js
RUN dotnet publish RiffRoster.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false
RUN dotnet publish tools/RiffRoster.DataMigration/RiffRoster.DataMigration.csproj \
    --configuration Release --output /app/migration --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app-runtime
WORKDIR /app
RUN mkdir -p /data/backups /data/keys

COPY --from=build /app/publish .
COPY --from=build /app/migration ./migration

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

ENTRYPOINT ["dotnet", "RiffRoster.dll"]

# Optional migration/backup worker image: build --target operations.
FROM app-runtime AS operations
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl \
    && mkdir -p /usr/share/postgresql-common/pgdg \
    && curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update && apt-get install -y --no-install-recommends postgresql-client-17 \
    && rm -rf /var/lib/apt/lists/*
ENV RIFFROSTER_PG_BIN=/usr/lib/postgresql/17/bin
ENTRYPOINT ["dotnet", "/app/migration/RiffRoster.DataMigration.dll"]

# Keep the default Railway build as the web application.
FROM app-runtime AS runtime
