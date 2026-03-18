---
title: Introduction
description: Overview of mc-operator — what it is, what it solves, and when to use it.
sidebar:
  order: 1
---

**mc-operator** is a production-grade [Kubernetes Operator](https://kubernetes.io/docs/concepts/extend-kubernetes/operator/) for managing Minecraft server deployments. It is built with [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/) and [KubeOps 10](https://github.com/buehler/dotnet-operator-sdk), and is maintained under the [dhv.sh](https://dhv.sh) umbrella.

## What problem does it solve?

Running a Minecraft server on Kubernetes without an operator means manually writing and maintaining StatefulSets, Services, PersistentVolumeClaims, ConfigMaps, and webhook configs — then keeping them all consistent across environments. This is error-prone and tedious.

mc-operator turns server administration into a single Kubernetes resource:

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
  jvm:
    initialMemory: "2G"
    maxMemory: "4G"
  storage:
    size: "20Gi"
```

The operator takes care of the rest: provisioning storage, configuring the server, exposing it on the network, and continuously reconciling the desired state.

## Supported server distributions

| Type    | Description |
|---------|-------------|
| `Vanilla` | Official Mojang server jar |
| `Paper`   | High-performance Paper fork — recommended for production |
| `Spigot`  | Spigot with Bukkit API support |
| `Bukkit`  | CraftBukkit server |

All distributions are backed by the community-standard [`itzg/minecraft-server`](https://github.com/itzg/docker-minecraft-server) Docker image, which handles version management and configuration via environment variables.

## Key capabilities

- **CRD-driven**: `MinecraftServer` is a first-class Kubernetes resource with a typed spec and status.
- **Admission webhooks**: A validating webhook rejects invalid configurations before they reach the cluster. A mutating webhook applies sensible defaults.
- **Safe data handling**: PVCs are retained by default when a server is deleted, preventing accidental data loss.
- **Pause/resume**: Set `spec.replicas: 0` to pause a server and retain all world data. Set back to `1` to resume.
- **Status reporting**: The operator continuously reports the server's phase (`Pending`, `Provisioning`, `Running`, `Paused`, `Failed`), connection endpoint, and PVC status.
- **Leader election**: Deploy multiple operator replicas safely using built-in leader election.
- **Helm chart**: Clean, versioned Helm chart published to GHCR as an OCI artifact.

## What it is not

mc-operator is a **lifecycle manager** — it manages server infrastructure, not game content. It does not:

- Install or manage plugins
- Handle automated world backups
- Provide RCON command routing
- Support multi-server proxy topologies (BungeeCord, Velocity)

These are intentional v1 scope decisions. See the [Roadmap](/mc-operator/about/roadmap/) for future plans.
