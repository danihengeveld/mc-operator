---
title: Architecture
description: Design decisions, reconciliation model, and internal structure of mc-operator.
sidebar:
  order: 1
---

import { Aside } from '@astrojs/starlight/components';

## Overview

mc-operator is a [Kubernetes Operator](https://kubernetes.io/docs/concepts/extend-kubernetes/operator/) built with .NET 10 and [KubeOps 10](https://github.com/buehler/dotnet-operator-sdk). It manages `MinecraftServer` custom resources through a continuous reconciliation loop, ensuring the actual cluster state matches the desired state expressed in the CRD spec.

### API group

The operator uses the API group `minecraft.hengeveld.dev`. This domain is owned and maintained under the [hengeveld.dev](https://hengeveld.dev) umbrella.

## Repository layout

```
mc-operator/
├── .github/
│   ├── dependabot.yml          # Automated dependency updates
│   └── workflows/
│       ├── ci.yml              # Build + test on push/PR
│       ├── release-image.yml   # Publish container image on tag
│       └── release-chart.yml   # Package and publish Helm chart on tag
├── charts/
│   └── mc-operator/            # Helm chart (OCI-published to GHCR)
├── docs/                       # Astro Starlight documentation site
├── examples/                   # Example MinecraftServer manifests
├── manifests/
│   ├── crd/                    # CustomResourceDefinition YAML
│   ├── rbac/                   # ClusterRole + ClusterRoleBinding
│   └── operator/               # Deployment, Service, webhook configs (Kustomize)
└── src/
    ├── McOperator/             # Main operator application
    └── McOperator.Tests/       # Unit tests (TUnit)
```

## Core components

### MinecraftServer CRD

The `MinecraftServer` CRD (`minecraft.hengeveld.dev/v1alpha1`) is the operator's public API. Every field in the spec maps directly to a Kubernetes resource or container configuration. The CRD is fully documented in the [CRD reference](/mc-operator/reference/crd/).

### Controller (reconciler)

`MinecraftServerController` implements `IEntityController<MinecraftServer>`. On each reconcile it:

1. Sets the status phase to `Provisioning`
2. Reconciles the **ConfigMap** (audit-visible `server.properties`)
3. Reconciles the **Service** (ClusterIP/NodePort/LoadBalancer)
4. Reconciles the **StatefulSet** (the actual server workload)
5. Reads StatefulSet status to determine the current phase
6. Updates the `status` subresource with endpoint and PVC info

All child resources are labeled with owner references, so they are automatically garbage-collected when the parent `MinecraftServer` is deleted.

### Admission webhooks

**Validating webhook** (`MinecraftServerValidationWebhook`): Rejects invalid specs at admission time, before resources are created or updated. It validates:
- EULA acceptance
- Minecraft version not blank
- JVM memory values parseable and `maxMemory >= initialMemory`
- NodePort value only provided for NodePort service type
- NodePort in the 30000–32767 range
- Storage size a valid Kubernetes resource quantity
- Mount path absolute
- Immutable fields not changed on update (`storage.enabled`, `storage.size`, `storage.storageClassName`)
- Server port and view distance in range

**Mutating webhook** (`MinecraftServerMutationWebhook`): Normalizes specs on create/update:
- Applies a default MOTD based on server name and type if not set
- Normalizes `levelType` to uppercase
- Trims whitespace from string fields

### Finalizer

`MinecraftServerFinalizer` runs during deletion when `spec.storage.deleteWithServer: true`. It:
1. Identifies the PVC created by the StatefulSet (`data-<name>-0`)
2. Deletes it explicitly (PVCs from VolumeClaimTemplates are not owned by the MinecraftServer, so they won't be garbage-collected automatically)

When `deleteWithServer: false` (the default), the finalizer runs but intentionally does nothing — the PVC is retained.

## Key design decisions

### StatefulSet over Deployment

Minecraft is a stateful singleton. StatefulSets provide:
- **Stable pod identity** (`server-name-0`): deterministic PVC naming
- **VolumeClaimTemplates**: native pod-to-PVC relationship management
- **Ordered updates**: prevents concurrent pod replacement that could corrupt state
- **`WhenScaled: Retain`**: PVC not deleted on scale-down

### itzg/minecraft-server as the base image

Rather than maintaining separate images per distribution, the operator sets the `TYPE` and `VERSION` environment variables on the `itzg/minecraft-server` image. This image is the community standard, handles all distributions, and is actively maintained.

Users can override with `spec.image` for full control (e.g. custom images with pre-installed plugins).

### Data safety defaults

PVCs are **retained by default** on deletion (`deleteWithServer: false`). World data is irreplaceable; the operational cost of a retained PVC is negligible compared to the risk of accidental deletion.

Storage fields (`enabled`, `size`, `storageClassName`) are **immutable** after creation, enforced by the validating webhook. This matches PVC semantics: you cannot resize a claim once bound.

### API version (v1alpha1)

The API is at `v1alpha1` even though the software is a working v1. This signals that the spec may evolve before it stabilizes. The graduation path is: `v1alpha1 → v1beta1 → v1`.

## Reconciliation model

```
Reconcile(server):
  1. status.phase = Provisioning
  2. ConfigMap: get-or-create / update
  3. Service:   get-or-create / update
  4. StatefulSet: get-or-create / update
  5. Read StatefulSet.status.readyReplicas
     - If replicas==0 → phase=Paused
     - If readyReplicas==1 → phase=Running
     - Else → phase=Provisioning
  6. Update status (phase, endpoint, PVC info, conditions)
  7. Requeue after 5m (drift detection)
```

On error, the controller requeues after 30 seconds.
