---
title: Roadmap
description: Known limitations, planned improvements, and the future direction of mc-operator.
sidebar:
  order: 1
---

## Current scope (v1)

mc-operator v1 is a production-grade **server lifecycle manager**. It handles provisioning, configuration, persistence, and exposure of individual Minecraft server instances on Kubernetes. It also supports **multi-server clusters** with a managed Velocity proxy via the `MinecraftServerCluster` CRD.

It deliberately does **not** include game-content concerns (plugins, modpacks, backups, RCON). These are significant surface areas that need careful design. See the rationale in the [Architecture guide](/mc-operator/guides/architecture/).

## Known limitations

| Limitation | Notes |
|------------|-------|
| No plugin management | Users manage plugins via init containers, custom images, or exec |
| No automated backups | Use Velero or manual `kubectl cp` from a paused server |
| No RCON integration | Use `kubectl exec` for server commands |
| Single-replica per server | Individual Minecraft servers are not horizontally scalable |
| Storage size immutable | Resize PVCs directly via `kubectl patch` |
| Dynamic scaling framework only | Scaling policy is validated and stored, but metric collection is not yet implemented |

## Completed

| Feature | Notes |
|---------|-------|
| Multi-server proxy (Velocity) | `MinecraftServerCluster` CRD with managed Velocity proxy, Modern forwarding, and automatic server registration |
| Scaling framework | Static mode (fixed replicas) and Dynamic mode structure (min/max/policy) — metric-driven scaling pending |
| Zero-downtime version upgrades | Pre-pull Job pulls new OCI image layers and pre-downloads the Minecraft server jar to the data PVC while the server is still running; rolling update only waits for server startup |

## Planned for future releases

These are possibilities, not commitments. Priority depends on community interest and use cases.

### Metric collection for dynamic scaling
Collect player count metrics from backend servers to drive auto-scaling decisions. The `ScalingPolicy` spec is already in place; the missing piece is a metric collection sidecar or RCON-based polling.

### Plugin management
A structured way to declare plugin JARs in the spec. Likely via init containers or a sidecar pattern that downloads plugins from URLs or a plugin registry.

### Automated backups
A `BackupSchedule` CRD or integration with the Kubernetes VolumeSnapshot API. Goals: scheduled backups, configurable retention, restore support.

### RCON integration
Allow the operator to execute server commands via RCON on spec changes (e.g. whitelist updates without restarts). Requires secure credential management.

### Prometheus metrics
Export server metrics (player count, TPS, memory usage) via the itzg/minecraft-server metrics endpoint and expose them as standard Prometheus metrics.

### Version auto-update
Watch for new Minecraft releases and optionally trigger updates automatically (with a `updatePolicy` field in the spec).

### Bedrock server support
Manage [Bedrock dedicated servers](https://www.minecraft.net/en-us/download/server/bedrock) alongside Java edition.

### API graduation
Graduate the API from `v1alpha1` to `v1beta1` once the spec proves stable in production, and eventually to `v1`.

## Contributing to the roadmap

Have a use case that isn't covered? Open an issue on [GitHub](https://github.com/danihengeveld/mc-operator/issues) describing the problem. Roadmap priorities are shaped by real use cases.
