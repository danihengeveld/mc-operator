---
title: CRD Reference
description: Complete reference for the MinecraftServer custom resource — spec, status, and all fields.
sidebar:
  order: 1
---

import { Aside } from '@astrojs/starlight/components';

## MinecraftServer

**API Group/Version**: `minecraft.hengeveld.dev/v1alpha1`  
**Kind**: `MinecraftServer`  
**Short name**: `mcs`  
**Scope**: Namespaced

---

## Spec

### Top-level fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `acceptEula` | `boolean` | ✅ | `false` | Must be `true` to start the server. You accept the [Minecraft EULA](https://www.minecraft.net/en-us/eula). |
| `server` | `ServerSpec` | ✅ | — | Server distribution and version. |
| `properties` | `ServerPropertiesSpec` | | defaults | Game settings (`server.properties`). |
| `jvm` | `JvmSpec` | | defaults | JVM heap and flags. |
| `resources` | `ResourcesSpec` | | defaults | Kubernetes resource requests/limits. |
| `storage` | `StorageSpec` | | defaults | Persistent storage configuration. |
| `service` | `ServiceSpec` | | defaults | Service type and exposure settings. |
| `image` | `string` | | — | Override the container image. If unset, uses `itzg/minecraft-server:latest`. |
| `imagePullPolicy` | `string` | | `IfNotPresent` | `Always`, `IfNotPresent`, or `Never`. |
| `replicas` | `integer` | | `1` | `0` (paused) or `1` (running). Minecraft servers are single-instance. |

---

### `server`

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `type` | `string` | ✅ | `Vanilla` | Distribution: `Vanilla`, `Paper`, `Spigot`, `Bukkit`. |
| `version` | `string` | | `LATEST` | Minecraft version, e.g. `1.20.4`. `LATEST` always pulls the newest release. |

---

### `properties`

All fields map directly to `server.properties` keys.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `motd` | `string` | — | Message of the day shown in the server list. |
| `difficulty` | `string` | `Easy` | `Peaceful`, `Easy`, `Normal`, `Hard`. |
| `gameMode` | `string` | `Survival` | `Survival`, `Creative`, `Adventure`, `Spectator`. |
| `maxPlayers` | `integer` | `20` | Maximum concurrent players (1–10000). |
| `onlineMode` | `boolean` | `true` | Authenticate with Mojang. Set `false` for offline/cracked mode. |
| `whitelistEnabled` | `boolean` | `false` | Only allow whitelisted players. |
| `whitelist` | `string[]` | `[]` | Player usernames to whitelist. |
| `ops` | `string[]` | `[]` | Player usernames to grant operator status. |
| `pvp` | `boolean` | `true` | Allow player-vs-player combat. |
| `hardcore` | `boolean` | `false` | Hardcore mode (one life per player). |
| `spawnProtection` | `integer` | `16` | Spawn protection radius in blocks (0 to disable). |
| `levelSeed` | `string` | — | World seed. Empty = random. |
| `levelType` | `string` | `DEFAULT` | `DEFAULT`, `FLAT`, `LARGBIOMES` (note: no E — matches itzg/minecraft-server), `AMPLIFIED`, `BUFFET`. |
| `levelName` | `string` | `world` | World directory name. |
| `viewDistance` | `integer` | `10` | View distance in chunks (3–32). |
| `simulationDistance` | `integer` | `10` | Simulation distance in chunks (3–32). |
| `generateStructures` | `boolean` | `true` | Generate villages, strongholds, etc. |
| `allowNether` | `boolean` | `true` | Enable the Nether dimension. |
| `serverPort` | `integer` | `25565` | Port inside the container (1024–65535). |
| `additionalProperties` | `map[string]string` | `{}` | Raw `server.properties` key/value overrides. Applied last. |

---

### `jvm`

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `initialMemory` | `string` | `512m` | Initial heap size (`-Xms`). E.g. `512m`, `1G`. |
| `maxMemory` | `string` | `1G` | Maximum heap size (`-Xmx`). E.g. `2G`, `4G`. |
| `extraJvmArgs` | `string[]` | `[]` | Additional JVM flags (e.g. GC tuning flags). |

<Aside type="tip">
For Paper servers on production hardware, consider G1GC flags:
```yaml
jvm:
  extraJvmArgs:
    - "-XX:+UseG1GC"
    - "-XX:G1HeapRegionSize=4M"
    - "-XX:+UnlockExperimentalVMOptions"
    - "-XX:MaxGCPauseMillis=200"
```
</Aside>

---

### `resources`

Standard Kubernetes resource requests and limits for the Minecraft server container.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `cpuRequest` | `string` | `500m` | CPU request. |
| `cpuLimit` | `string` | — | CPU limit. No limit by default. |
| `memoryRequest` | `string` | `1Gi` | Memory request. |
| `memoryLimit` | `string` | = `memoryRequest` | Memory limit. Defaults to the request value if not set. |

<Aside type="caution">
Always set `memoryLimit` at least as large as `jvm.maxMemory` plus 256–512 Mi for JVM overhead. A limit smaller than the JVM heap will cause OOMKilled pods.
</Aside>

---

### `storage`

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `enabled` | `boolean` | `true` | Create a PVC for server data. Set `false` only for ephemeral/testing deployments. Immutable after creation. |
| `size` | `string` | `10Gi` | PVC size. Immutable after creation. |
| `storageClassName` | `string` | — | StorageClass name. Empty = cluster default. Immutable after creation. |
| `deleteWithServer` | `boolean` | `false` | **Danger**: delete the PVC when the MinecraftServer is deleted. Defaults to `false` (retain data). |
| `mountPath` | `string` | `/data` | Mount path inside the container. |

<Aside type="danger">
`storage.enabled`, `storage.size`, and `storage.storageClassName` are **immutable** after creation. Changes to these fields are rejected by the validating webhook. If you need to resize storage, resize the PVC directly using `kubectl patch`.
</Aside>

---

### `service`

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `type` | `string` | `ClusterIP` | `ClusterIP`, `NodePort`, or `LoadBalancer`. |
| `nodePort` | `integer` | — | Node port (30000–32767). Only valid when `type: NodePort`. |
| `annotations` | `map[string]string` | `{}` | Service annotations (e.g. cloud load balancer configuration). |

---

## Status

The `status` subresource is managed by the operator and reflects the current observed state.

| Field | Type | Description |
|-------|------|-------------|
| `phase` | `string` | Lifecycle phase: `Pending`, `Provisioning`, `Running`, `Paused`, `Failed`, `Terminating`. |
| `message` | `string` | Human-readable description of the current state. |
| `observedGeneration` | `integer` | The spec generation that was last reconciled. |
| `readyReplicas` | `integer` | Number of ready replicas (0 or 1). |
| `currentImage` | `string` | The container image currently in use. |
| `currentVersion` | `string` | The Minecraft version string. |
| `endpoint.host` | `string` | IP address or hostname for connecting to the server. |
| `endpoint.port` | `integer` | Port for the Minecraft protocol. |
| `endpoint.connectionString` | `string` | `host:port` connection string. |
| `storage.name` | `string` | PVC name. |
| `storage.phase` | `string` | PVC phase (`Bound`, `Pending`, etc.). |
| `storage.storageClassName` | `string` | Storage class in use. |
| `storage.capacity` | `string` | Actual allocated capacity. |
| `conditions` | `Condition[]` | Standard Kubernetes conditions (`Available`, `Progressing`, `Degraded`). |

### Phase lifecycle

```
Pending → Provisioning → Running
                       ↕
                     Paused  (replicas=0)
                       ↓
                     Failed  (unrecoverable error)
```

On deletion, the phase transitions to `Terminating` while the finalizer runs.
