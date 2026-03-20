# Dynamic Scaling Plan: Metric Collection & Auto-Scaling

This document describes the planned architecture for implementing dynamic (auto) scaling
in the `MinecraftServerCluster` CRD, building on the static scaling and Velocity proxy
framework already in place.

---

## Current State

The `MinecraftServerCluster` CRD supports two scaling modes:

- **Static**: Fixed number of replicas. Fully implemented.
- **Dynamic**: Min/max replicas with a scaling policy. Framework is in place (spec, validation,
  controller scaffolding), but the cluster currently runs at `minReplicas` because metric
  collection is not yet implemented.

## Goal

Implement metric-driven auto-scaling that:
1. Collects player count metrics from running backend servers
2. Computes the desired replica count based on the configured scaling policy
3. Scales backend servers up/down within the min/max bounds
4. Updates the Velocity proxy configuration when servers are added/removed

---

## Metric Collection

### Option A: RCON Query (Recommended)

**How it works**: The operator connects to each backend server via RCON and runs the `list`
command, which returns the current player count.

**Pros**:
- Direct, reliable data from the server itself
- No additional sidecar or agent needed
- RCON is supported by all server distributions (Vanilla, Paper, Spigot, Bukkit)
- The `itzg/minecraft-server` image exposes RCON on port 25575 by default

**Cons**:
- Requires RCON to be enabled on backend servers (operator would configure this automatically)
- Requires RCON password management (operator generates and stores in a Secret)
- Adds a network dependency between operator and game server pods

**Implementation**:
1. Add `rcon-port` (default 25575) and `enable-rcon=true` to backend server env vars
2. Generate a random RCON password per cluster, store in a Kubernetes Secret
3. Use an RCON client library (or implement the simple RCON protocol) to query servers
4. Query runs on each reconciliation cycle (5 minutes) and optionally on a shorter metrics loop

### Option B: Prometheus Metrics Sidecar

**How it works**: Deploy a metrics exporter sidecar (e.g., `itzg/mc-monitor`) alongside each
backend server. The operator scrapes Prometheus metrics from the sidecar.

**Pros**:
- Rich metrics (TPS, entity count, memory, not just player count)
- Standard Prometheus format, integrates with existing monitoring
- Could also feed into Kubernetes HPA for secondary scaling

**Cons**:
- Adds a sidecar container to every backend server (resource overhead)
- More complex deployment model
- Requires service discovery or additional labels for scraping

### Option C: Kubernetes Metrics API (via Custom Metrics)

**How it works**: Use the Kubernetes Custom Metrics API (`custom.metrics.k8s.io`) to expose
player count as a metric, then use it in scaling decisions.

**Pros**:
- Kubernetes-native approach
- Could leverage existing HPA infrastructure

**Cons**:
- Requires a custom metrics adapter (Prometheus Adapter or similar)
- Heavy infrastructure dependency for a simple metric
- Over-engineered for the use case

### Recommendation

**Option A (RCON) for initial implementation**, with a path to Option B for richer metrics
in the future. Rationale:
- Simplest to implement (no sidecars, no external dependencies)
- Sufficient for the primary use case (player count scaling)
- RCON is already available in all supported server distributions
- Can be extended to Option B later without breaking changes

---

## Scaling Algorithm

### Input
- `minReplicas`: Minimum number of backend servers (always running)
- `maxReplicas`: Maximum number of backend servers
- `targetPercentage`: Target utilization percentage (e.g., 75%)
- `maxPlayers`: Per-server max player count (from template.properties.maxPlayers)
- Current player counts per server (from metric collection)

### Computation

```
totalPlayers = sum(playerCount for each server)
totalCapacity = currentReplicas * maxPlayers
currentUtilization = totalPlayers / totalCapacity * 100

if currentUtilization > targetPercentage:
    # Scale up: add servers to bring utilization below target
    desiredCapacity = totalPlayers / (targetPercentage / 100)
    desiredReplicas = ceil(desiredCapacity / maxPlayers)
elif currentUtilization < (targetPercentage - hysteresis):
    # Scale down: remove empty servers
    desiredCapacity = totalPlayers / (targetPercentage / 100)
    desiredReplicas = max(ceil(desiredCapacity / maxPlayers), minReplicas)
else:
    desiredReplicas = currentReplicas  # No change

desiredReplicas = clamp(desiredReplicas, minReplicas, maxReplicas)
```

### Hysteresis

A hysteresis band (e.g., 10%) prevents thrashing:
- Scale up when utilization > targetPercentage
- Scale down when utilization < (targetPercentage - 10%)
- No action in between

### Cooldown

To prevent rapid scale events:
- **Scale-up cooldown**: 60 seconds (quick response to demand)
- **Scale-down cooldown**: 300 seconds (conservative, avoid premature removal)
- Track `lastScaleTime` in status

### Safe Scale-Down

Before removing a server:
1. Check if it has 0 players
2. If players are present, mark it as "draining" — remove from Velocity `try` list so no new
   players are routed to it
3. Wait for players to disconnect or be transferred
4. Only delete the server once player count reaches 0

---

## Implementation Plan

### Phase 1: RCON Infrastructure (PR 1)
- [ ] Add RCON port and password configuration to backend server template
- [ ] Generate RCON password per cluster, store in Kubernetes Secret
- [ ] Add RCON enable env vars to backend server StatefulSets
- [ ] Implement RCON client (simple TCP protocol for `list` command)
- [ ] Add unit tests for RCON client

### Phase 2: Metric Collection Loop (PR 2)
- [ ] Add a metrics collection service that queries player counts via RCON
- [ ] Store collected metrics in cluster status (per-server player counts)
- [ ] Add `lastMetricCollection` timestamp to status
- [ ] Integrate metrics collection into the reconciliation loop
- [ ] Add metrics collection interval to cluster spec (default: 60s)

### Phase 3: Scaling Decision Engine (PR 3)
- [ ] Implement the scaling algorithm (compute desired replica count)
- [ ] Add hysteresis and cooldown logic
- [ ] Add `lastScaleTime` and `lastScaleDirection` to status
- [ ] Integrate scaling decisions into the reconciliation loop
- [ ] Add unit tests for the scaling algorithm

### Phase 4: Safe Scale-Down (PR 4)
- [ ] Implement server draining (remove from Velocity try list)
- [ ] Wait for player count to reach 0 before deletion
- [ ] Add draining status per server in cluster status
- [ ] Add configurable drain timeout (force-delete after timeout)

### Phase 5: Observability (PR 5)
- [ ] Emit Kubernetes Events on scale up/down decisions
- [ ] Add scaling-related conditions to cluster status
- [ ] Optional: Prometheus metrics for scaling decisions

---

## Spec Changes

```csharp
public class ScalingPolicy
{
    // Existing
    public ScalingMetric Metric { get; set; } = ScalingMetric.PlayerCount;
    public int TargetPercentage { get; set; } = 80;

    // New fields for Phase 2+
    /// <summary>
    /// How often to collect metrics, in seconds. Default: 60.
    /// </summary>
    public int MetricIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Hysteresis percentage for scale-down decisions. Default: 10.
    /// Scale down only when utilization drops below (TargetPercentage - Hysteresis).
    /// </summary>
    public int HysteresisPercentage { get; set; } = 10;

    /// <summary>
    /// Cooldown period after a scale-up event, in seconds. Default: 60.
    /// </summary>
    public int ScaleUpCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Cooldown period after a scale-down event, in seconds. Default: 300.
    /// </summary>
    public int ScaleDownCooldownSeconds { get; set; } = 300;
}
```

## Status Changes

```csharp
public class MinecraftServerClusterStatus
{
    // Existing fields...

    // New fields for dynamic scaling
    /// <summary>
    /// Timestamp of the last scaling event.
    /// </summary>
    public DateTime? LastScaleTime { get; set; }

    /// <summary>
    /// Direction of the last scaling event ("up" or "down").
    /// </summary>
    public string? LastScaleDirection { get; set; }

    /// <summary>
    /// Timestamp of the last successful metric collection.
    /// </summary>
    public DateTime? LastMetricCollection { get; set; }

    /// <summary>
    /// Current aggregate player count across all servers.
    /// </summary>
    public int TotalPlayers { get; set; }

    /// <summary>
    /// Per-server player count (updated by metric collection).
    /// </summary>
    public IList<ServerMetrics> ServerMetrics { get; set; }
}

public class ServerMetrics
{
    public string? Name { get; set; }
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public bool Draining { get; set; }
}
```

---

## Critical Design Questions

### Q: Should scaling use a separate reconciliation loop?

**Answer**: Yes. The main reconciliation loop runs every 5 minutes (or on spec changes).
Metric collection and scaling decisions should run on a shorter interval (configurable,
default 60s) to respond to player demand in a timely manner. This can be implemented as
a KubeOps timer or a background service.

### Q: How do we handle Velocity proxy config updates during scaling?

**Answer**: When servers are added/removed, the proxy ConfigMap must be updated with the
new server list. The proxy Deployment should then be rolled (annotation change on pod
template) to pick up the new config. This is already handled by the existing
`ReconcileProxyConfigMap` and `ReconcileProxyDeployment` methods.

### Q: What happens to players on a server that's being removed?

**Answer**: Velocity supports `failover-on-unexpected-server-disconnect = true` (already
configured). When a backend server is stopped, connected players are moved to the next
server in the try list. For a graceful experience, the draining mechanism (Phase 4) first
removes the server from the try list, so no new players join, then existing players
naturally disconnect or are transferred.

### Q: Should we support HPA integration instead of built-in scaling?

**Answer**: No, at least not initially. HPA requires custom metrics adapters and is designed
for stateless workloads. Our scaling needs game-aware logic (player counts, safe drain)
that HPA cannot express. We may expose metrics for HPA consumption later as an
alternative scaling method.
