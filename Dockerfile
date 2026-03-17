# ─── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

# Restore dependencies in a separate layer for caching
COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/McOperator/McOperator.csproj src/McOperator/packages.lock.json src/McOperator/
RUN dotnet restore src/McOperator/McOperator.csproj --locked-mode

# Build and publish
COPY src/ src/
RUN dotnet publish src/McOperator/McOperator.csproj \
    -c Release \
    -o /app \
    --no-restore \
    --property:PublishTrimmed=false

# ─── Runtime stage (Ubuntu Noble Chiseled) ────────────────────────────────────
# noble-chiseled is an ultra-minimal, distroless-style image:
#   - No shell, no package manager, minimal attack surface
#   - Runs as built-in non-root `app` user (UID 1654) — no useradd needed
#   - Includes ASP.NET Core runtime and ICU globalization libraries
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final

WORKDIR /app

COPY --chown=app:app --from=build /app ./

# app user (UID 1654) is the default in noble-chiseled images
USER app

ENTRYPOINT ["dotnet", "McOperator.dll"]
