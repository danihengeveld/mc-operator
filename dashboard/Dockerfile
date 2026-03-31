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
# Use a minimal Node.js image for production (TanStack Start runs on Node.js)
FROM node:24-alpine AS final

WORKDIR /app

# Copy built output, production server, and node_modules
COPY --from=build /build/dist ./dist
COPY --from=build /build/node_modules ./node_modules
COPY --from=build /build/package.json ./
COPY --from=build /build/start.mjs ./

# Run as non-root user
RUN addgroup -g 1001 -S dashboard && \
    adduser -u 1001 -S dashboard -G dashboard
USER dashboard

EXPOSE 3000

ENV HOST=0.0.0.0
ENV PORT=3000

ENTRYPOINT ["node", "start.mjs"]
