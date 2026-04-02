# ─── Build stage ──────────────────────────────────────────────────────────────
FROM oven/bun:1.3.11-alpine AS build
WORKDIR /build

# Install dependencies in a separate layer for caching
COPY package.json bun.lock ./
RUN bun install --frozen-lockfile

# Build the application
COPY . .
RUN bun run build

# ─── Runtime stage ────────────────────────────────────────────────────────────
# Distroless Bun image — minimal attack surface, ~43 MB
FROM oven/bun:1.3.11-distroless AS final

WORKDIR /app

# Copy built output, production server, and node_modules
COPY --from=build /build/dist ./dist
COPY --from=build /build/node_modules ./node_modules
COPY --from=build /build/package.json ./
COPY --from=build /build/server.ts ./

EXPOSE 3000

ENV HOST=0.0.0.0
ENV PORT=3000

ENTRYPOINT ["bun", "run", "server.ts"]
