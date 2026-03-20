---
title: Examples
description: Reference configurations for common Minecraft server deployments with mc-operator.
sidebar:
  order: 3
---

All example manifests are available in the [`examples/`](https://github.com/danihengeveld/mc-operator/tree/main/examples) directory.

## Vanilla server with LoadBalancer

A simple vanilla server with external access via a cloud load balancer:

```yaml
apiVersion: mc-operator.dhv.sh/v1alpha1
kind: MinecraftServer
metadata:
  name: vanilla-survival
  namespace: minecraft
spec:
  acceptEula: true
  server:
    type: Vanilla
    version: "1.20.4"
  properties:
    difficulty: Normal
    maxPlayers: 10
    onlineMode: true
  jvm:
    initialMemory: "1G"
    maxMemory: "2G"
  resources:
    cpuRequest: "500m"
    memoryRequest: "2Gi"
    memoryLimit: "2500Mi"
  storage:
    size: "20Gi"
  service:
    type: LoadBalancer
```

## Paper server with NodePort and whitelist

High-performance Paper server with a whitelist and G1GC tuning:

```yaml
apiVersion: mc-operator.dhv.sh/v1alpha1
kind: MinecraftServer
metadata:
  name: paper-survival
  namespace: minecraft
spec:
  acceptEula: true
  server:
    type: Paper
    version: "1.20.4"
  properties:
    motd: "§bPaper Survival §r— §eFast & Friendly"
    difficulty: Hard
    gameMode: Survival
    maxPlayers: 50
    onlineMode: true
    whitelistEnabled: true
    whitelist:
      - "TrustedPlayer1"
      - "TrustedPlayer2"
    pvp: true
    viewDistance: 12
    simulationDistance: 8
    ops:
      - "ServerAdmin"
    additionalProperties:
      enable-command-block: "true"
      max-world-size: "10000"
  jvm:
    initialMemory: "2G"
    maxMemory: "6G"
    extraJvmArgs:
      - "-XX:+UseG1GC"
      - "-XX:G1HeapRegionSize=4M"
      - "-XX:+UnlockExperimentalVMOptions"
      - "-XX:MaxGCPauseMillis=200"
  resources:
    cpuRequest: "2"
    cpuLimit: "4"
    memoryRequest: "6Gi"
    memoryLimit: "7Gi"
  storage:
    size: "50Gi"
    storageClassName: "fast-ssd"
  service:
    type: NodePort
    nodePort: 30565
```

## Small private Spigot server

A small server for a group of friends on a local network:

```yaml
apiVersion: mc-operator.dhv.sh/v1alpha1
kind: MinecraftServer
metadata:
  name: spigot-friends
  namespace: minecraft
spec:
  acceptEula: true
  server:
    type: Spigot
    version: "1.20.4"
  properties:
    motd: "Friends Only"
    difficulty: Normal
    maxPlayers: 5
    onlineMode: false       # Local network — no Mojang auth
    whitelistEnabled: true
    whitelist:
      - "Alice"
      - "Bob"
      - "Charlie"
    pvp: false
  jvm:
    initialMemory: "512m"
    maxMemory: "1G"
  resources:
    cpuRequest: "250m"
    memoryRequest: "1200Mi"
    memoryLimit: "1500Mi"
  storage:
    size: "10Gi"
  service:
    type: NodePort
```

## Creative Bukkit server with flat world

A creative building server on a superflat world:

```yaml
apiVersion: mc-operator.dhv.sh/v1alpha1
kind: MinecraftServer
metadata:
  name: bukkit-creative
  namespace: minecraft
spec:
  acceptEula: true
  server:
    type: Bukkit
    version: "1.20.4"
  properties:
    motd: "§aCreative Build Server"
    difficulty: Peaceful
    gameMode: Creative
    maxPlayers: 20
    levelType: FLAT
    levelName: creative-flat
    generateStructures: false
    pvp: false
    allowNether: false
  jvm:
    initialMemory: "512m"
    maxMemory: "1G"
  resources:
    cpuRequest: "250m"
    memoryRequest: "1200Mi"
    memoryLimit: "1500Mi"
  storage:
    size: "10Gi"
  service:
    type: ClusterIP
```

## Paused server (data retained)

A server that has been intentionally paused — zero replicas keeps the pod stopped without deleting the world:

```yaml
apiVersion: mc-operator.dhv.sh/v1alpha1
kind: MinecraftServer
metadata:
  name: paper-survival
  namespace: minecraft
spec:
  acceptEula: true
  replicas: 0      # ← Pause: pod is stopped, PVC and data are retained
  server:
    type: Paper
    version: "1.20.4"
  storage:
    size: "50Gi"
  service:
    type: NodePort
    nodePort: 30565
```

Resume by patching `spec.replicas: 1`:

```bash
kubectl patch minecraftserver paper-survival -n minecraft \
  --type merge -p '{"spec": {"replicas": 1}}'
```

## Minecraft cluster with static scaling

Three Paper servers behind a Velocity proxy with external access via LoadBalancer:

```yaml
apiVersion: mc-operator.dhv.sh/v1alpha1
kind: MinecraftServerCluster
metadata:
  name: survival-cluster
  namespace: minecraft
spec:
  template:
    acceptEula: true
    server:
      type: Paper
      version: "1.20.4"
    properties:
      difficulty: Normal
      maxPlayers: 50
    jvm:
      initialMemory: "1G"
      maxMemory: "3G"
    resources:
      cpuRequest: "1"
      memoryRequest: "3Gi"
    storage:
      size: "20Gi"
  scaling:
    mode: Static
    replicas: 3
  proxy:
    proxyPort: 25577
    maxPlayers: 150
    onlineMode: true
    playerForwardingMode: Modern
    service:
      type: LoadBalancer
```

## Minecraft cluster with dynamic scaling

An auto-scaling cluster that grows and shrinks based on player count:

```yaml
apiVersion: mc-operator.dhv.sh/v1alpha1
kind: MinecraftServerCluster
metadata:
  name: dynamic-cluster
  namespace: minecraft
spec:
  template:
    acceptEula: true
    server:
      type: Paper
      version: "1.20.4"
    properties:
      maxPlayers: 30
    jvm:
      maxMemory: "2G"
    storage:
      size: "10Gi"
  scaling:
    mode: Dynamic
    minReplicas: 1
    maxReplicas: 5
    policy:
      metric: PlayerCount
      targetPercentage: 75
  proxy:
    proxyPort: 25577
    maxPlayers: 150
    playerForwardingMode: Modern
    service:
      type: NodePort
      nodePort: 30577
```
