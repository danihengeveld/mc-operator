---
title: Configuration Guide
description: Patterns, recipes, and best practices for configuring MinecraftServer resources.
sidebar:
  order: 2
---

import { Aside } from '@astrojs/starlight/components';

This guide covers common configuration patterns and best practices for running Minecraft servers with mc-operator.

## Memory sizing

Memory is the most important resource to size correctly. Minecraft is memory-intensive, and undersized heaps cause lag.

**Minimum for a small private server (1–5 players):**
```yaml
jvm:
  initialMemory: "1G"
  maxMemory: "2G"
resources:
  memoryRequest: "2Gi"
  memoryLimit: "2Gi"   # At least maxMemory + 512Mi overhead
```

**Production Paper server (10–50 players):**
```yaml
jvm:
  initialMemory: "2G"
  maxMemory: "4G"
  extraJvmArgs:
    - "-XX:+UseG1GC"
    - "-XX:G1HeapRegionSize=4M"
    - "-XX:+UnlockExperimentalVMOptions"
    - "-XX:MaxGCPauseMillis=200"
    - "-XX:+DisableExplicitGC"
    - "-XX:+AlwaysPreTouch"
resources:
  cpuRequest: "1"
  cpuLimit: "4"
  memoryRequest: "4500Mi"   # maxMemory + ~500Mi JVM overhead
  memoryLimit: "4500Mi"
```

<Aside type="caution">
The `memoryLimit` must always exceed `jvm.maxMemory`. A container limit smaller than the JVM heap causes immediate OOMKilled termination. Add at least 256–512 Mi of overhead.
</Aside>

## Storage configuration

### Recommended production storage

```yaml
storage:
  enabled: true
  size: "20Gi"                # Generous for mods/world growth
  storageClassName: "fast-ssd" # Use a fast SSD class if available
  deleteWithServer: false       # Always retain data (the default)
```

### Sizing guidance

| Server type | Recommended size | Notes |
|-------------|-----------------|-------|
| Private/small | 10–20 Gi | Vanilla worlds grow slowly |
| Semi-public Paper | 20–50 Gi | Plugins add data |
| Large/public | 50 Gi+ | Allow room for world growth |

### Retaining data safely

By default, the PVC is **retained** when the MinecraftServer is deleted. This is the right default for production.

If you delete a `MinecraftServer` and want to restore data later, the PVC remains in the namespace:

```bash
kubectl get pvc -n minecraft
# data-my-server-0   Bound   ...
```

You can reconnect it by recreating the server with the same name — the StatefulSet will reattach to `data-<server-name>-0`.

## Server exposure

### ClusterIP (default)
For servers accessed only within the cluster or via `kubectl port-forward`:
```yaml
service:
  type: ClusterIP
```

### NodePort
For home labs or simple external access:
```yaml
service:
  type: NodePort
  nodePort: 30565   # Optional; auto-assigned if omitted
```

### LoadBalancer
For cloud environments with external load balancers:
```yaml
service:
  type: LoadBalancer
  annotations:
    # AWS NLB example
    service.beta.kubernetes.io/aws-load-balancer-type: "nlb"
    service.beta.kubernetes.io/aws-load-balancer-scheme: "internet-facing"
```

The server IP appears in `status.endpoint.host` once the load balancer is provisioned.

## Pausing a server

Set `replicas: 0` to pause a server without losing world data:

```yaml
spec:
  replicas: 0
```

The StatefulSet scales to zero, the pod stops, but the PVC remains intact. Resume by setting `replicas: 1`.

## Online vs offline mode

Online mode (default) authenticates players with Mojang's servers. Set `onlineMode: false` for:
- LAN/private servers with custom auth
- Development/testing

<Aside type="caution">
Offline mode disables player authentication. Any client can join with any username. Use whitelist or firewall rules to restrict access.
</Aside>

## Whitelist management

```yaml
properties:
  whitelistEnabled: true
  whitelist:
    - "PlayerOne"
    - "PlayerTwo"
```

Update the resource to add/remove players. The operator reconciles the running server accordingly.

## Op (operator) permissions

```yaml
properties:
  ops:
    - "AdminPlayer"
```

Ops have full in-game admin access. Changes are applied when the server reads the `ops` environment variable on startup.

## Custom server.properties overrides

For properties not directly exposed in the spec, use `additionalProperties`:

```yaml
properties:
  additionalProperties:
    enable-command-block: "true"
    max-world-size: "10000"
    spawn-monsters: "true"
    max-tick-time: "60000"
```

These are appended to `server.properties` and override any derived values from the spec.

## Using a custom image

To use your own Docker image (e.g. with pre-installed plugins):

```yaml
spec:
  image: "registry.example.com/my-minecraft:1.20.4"
  imagePullPolicy: Always
```

The operator skips the distribution-type logic and uses your image directly.

## World generation

```yaml
properties:
  levelSeed: "12345"         # Reproducible seed
  levelType: DEFAULT         # DEFAULT | FLAT | LARGBIOMES (no E, itzg convention) | AMPLIFIED
  levelName: "my-world"      # Directory name inside /data
  generateStructures: true
  allowNether: true
```
