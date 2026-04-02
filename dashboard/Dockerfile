# ─── Build stage ──────────────────────────────────────────────────────────────
FROM oven/bun:1.3.11-alpine AS build
WORKDIR /build

# Install dependencies in a separate layer for caching
COPY package.json bun.lock ./
RUN bun install --frozen-lockfile

# Build the application (Nitro bundles everything into .output/)
COPY . .
RUN bun run build

# ─── Runtime stage ────────────────────────────────────────────────────────────
# Distroless Bun image — minimal attack surface, ~43 MB
FROM oven/bun:1.3.11-distroless AS final

WORKDIR /app

# Copy the self-contained Nitro output
COPY --from=build /build/.output ./.output

EXPOSE 3000

ENV HOST=0.0.0.0
ENV PORT=3000

ENTRYPOINT ["bun", ".output/server/index.mjs"]
