# Architecture Notes: mc-operator v1

This document captures the key design decisions made for the v1 of the Minecraft Kubernetes Operator and the rationale behind each choice.

## Overview

`mc-operator` is a Kubernetes Operator built with .NET 10, maintained under the [dhv.sh](https://dhv.sh) umbrella and [KubeOps 10](https://github.com/buehler/dotnet-operator-sdk). It manages `MinecraftServer` custom resources, which each represent a fully configured Minecraft server deployment and all of its supporting Kubernetes resources.

---

## Decision 1: StatefulSet vs Deployment

**Choice: StatefulSet**

Minecraft servers are stateful singletons. The world data (chunks, player data, statistics) must persist across pod restarts. Using a StatefulSet provides:

- **Stable pod identity** (`server-name-0`): Makes PVC naming deterministic and predictable.
- **VolumeClaimTemplates**: The StatefulSet manages the relationship between pods and PVCs natively. When a pod is replaced, it reattaches to the same PVC.
- **Ordered rolling updates**: Prevents accidental concurrent replacement that could corrupt world state.
- **`WhenScaled: Retain` policy**: Even when scaling down, the PVC is not deleted. This is critical for safety.

A Deployment could technically work with a PVC, but the semantics are weaker (no guaranteed pod-to-PVC mapping, no ordered updates), and the StatefulSet model maps more naturally to the problem domain.

---

## Decision 2: Server Distribution Strategy

**Choice: itzg/minecraft-server image + environment variables**

Rather than maintaining separate Docker images for each Minecraft distribution (Vanilla, Paper, Spigot, Bukkit), the operator uses [`itzg/minecraft-server`](https://github.com/itzg/docker-minecraft-server) as the base image.

This image:
- Is the community standard for containerized Minecraft servers
- Supports all distributions we care about via the `TYPE` environment variable
- Handles server JAR downloading, version management, and `server.properties` generation
- Is actively maintained and widely used in production

The operator sets `TYPE=PAPER`, `TYPE=SPIGOT`, etc. via environment variables. Users can override with a custom image (`spec.image`) for full control.

This avoids:
- Maintaining N Docker images (one per distribution)
- Complex image-selection logic in the operator
- Lag behind upstream when new Minecraft versions release

**Trade-off**: We depend on a third-party image. The mitigation is that the image is extremely well-maintained and users can always override with their own image via `spec.image`.

---

## Decision 3: PVC Lifecycle and Data Safety

**Choice: Retain by default (`deleteWithServer: false`)**

Minecraft world data is irreplaceable. By default:
- When a `MinecraftServer` is deleted, the PVC is **not** deleted
- The StatefulSet uses `PersistentVolumeClaimRetentionPolicy.WhenDeleted = "Retain"`
- A finalizer logs a warning about the retained PVC

Users must explicitly opt in to PVC deletion by setting `spec.storage.deleteWithServer: true`.

This decision prioritizes **safety over convenience**. The operational cost of a retained PVC is minor (some storage cost), but the cost of accidentally deleted world data is catastrophic and unrecoverable.

**The finalizer** handles PVC cleanup in the `deleteWithServer: true` case. It runs before the StatefulSet is garbage-collected (via owner reference), ensuring the PVC is only deleted after the workload is stopped.

---

## Decision 4: CRD API Group and Version

**Choice: `mc-operator.dhv.sh/v1alpha1`**

Although this is "v1 of the software," the API version is `v1alpha1` because:
- The spec may evolve as we learn from production usage
- Breaking changes are expected before the API stabilizes
- Kubernetes conventions use `v1alpha1` for new APIs under development

The path to `v1` is: `v1alpha1` → `v1beta1` → `v1`, graduated as the API proves stable.

---

## Decision 5: Webhook Design

**Validating webhook**: Rejects invalid specs at admission time, before they reach the reconciler. This is the right place for:
- EULA acceptance enforcement (reject immediately rather than creating resources that fail)
- Cross-field validation (e.g., NodePort value only valid for NodePort service type)
- Immutable field checks (storage settings after creation)

**Mutating webhook**: Applies sensible defaults and normalization:
- Default MOTD based on server type
- Normalize LevelType to uppercase
- Trim whitespace from string fields

This ensures the controller always sees a normalized, validated spec.

**What validation does NOT do**: It does not check that the Minecraft version is valid for the distribution. This would require maintaining a list of compatible versions, which adds fragility. Version incompatibilities will surface in the container logs instead.

---

## Decision 6: Reconciliation Model

**Choice: IEntityController<T> with `ReconciliationResult<T>`**

The controller implements the KubeOps `IEntityController<MinecraftServer>` interface. Each reconcile:
1. Sets status to `Provisioning`
2. Reconciles ConfigMap → Service → StatefulSet in order
3. Reads StatefulSet status to determine the phase (Running/Provisioning/Paused/Failed)
4. Updates status with endpoint and PVC information

**Idempotency**: Each child resource reconciliation checks for existing resources by name and updates them if they exist. All resources are labeled with owner references, so Kubernetes garbage-collects them when the parent `MinecraftServer` is deleted.

**Requeue strategy**: On success, the controller requeues after 5 minutes to catch any drift. On failure, it requeues after 30 seconds for faster recovery.

---

## Decision 7: ConfigMap for Audit/Visibility

The controller creates a ConfigMap containing the derived `server.properties` content. This is not used by the container (which uses environment variables), but provides:
- Human-readable view of the effective server configuration
- Audit trail for what properties were applied
- A hook for future configuration injection

This is intentionally secondary to the environment variable mechanism.

---

## Decision 8: Replicas Field (0 or 1)

The spec includes a `replicas` field constrained to 0 or 1. Setting `replicas: 0` pauses the server without deleting world data. This is a common operational pattern for:
- Maintenance windows
- Cost savings during off-hours
- Temporary server shutdowns

The `Paused` phase in status reflects this state.

---

## Decision 9: Owner References vs Finalizers

**Owner references** are used for:
- ConfigMap, Service, StatefulSet → all owned by the MinecraftServer
- When MinecraftServer is deleted, Kubernetes automatically garbage-collects these

**Finalizer** is used for:
- PVC cleanup when `deleteWithServer: true`
- PVCs created by StatefulSet VolumeClaimTemplates are NOT owned by the MinecraftServer (they are managed by the StatefulSet), so they need explicit cleanup

This is the correct split: owner references for most resources, finalizers only for the case where we need custom cleanup logic.

---

## Decision 10: What Was Deliberately Deferred

### Plugins/Modpacks
Plugin management involves downloading JARs, managing versions, handling dependency conflicts. This is too complex for v1 without a clear product design. Users can manage plugins by:
- Using an init container to download plugins to the data volume
- Exec-ing into the pod to add plugins manually
- Building custom images with plugins pre-installed

### Backups
Automated backups require object storage credentials, backup scheduling, retention policies, and restore procedures. This is a significant surface area. We recommend using Velero for PVC snapshots.

### RCON
RCON integration would allow the operator to send commands to running servers. This requires network access to the RCON port and secure credential management. Deferred as `kubectl exec` suffices for v1.

### Horizontal Scaling / Proxy Support
Minecraft servers are not horizontally scalable in the traditional sense. Multi-server setups require proxy servers (BungeeCord, Velocity). This is a different product category from single-server management.

---

## Repository Structure

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
│       ├── Chart.yaml
│       ├── templates/
│       └── values.yaml
├── docs/                       # Astro Starlight documentation site
│   └── src/content/docs/       # All documentation pages
├── examples/                   # Example MinecraftServer manifests
├── manifests/
│   ├── crd/                    # CustomResourceDefinition YAML
│   ├── rbac/                   # ClusterRole + ClusterRoleBinding
│   └── operator/               # Deployment, Service, webhooks (Kustomize)
└── src/
    ├── McOperator/             # .NET 10 operator application
    │   ├── Builders/           # Resource builders (StatefulSet, Service, ConfigMap)
    │   ├── Controllers/        # Reconciliation controller
    │   ├── Entities/           # CRD entity types (spec, status)
    │   ├── Extensions/         # Extension methods
    │   ├── Finalizers/         # PVC cleanup finalizer
    │   ├── Webhooks/           # Validating/mutating webhooks
    │   └── Program.cs          # Entry point and DI registration
    └── McOperator.Tests/       # TUnit unit tests
        ├── ValidationWebhookTests.cs
        ├── StatefulSetBuilderTests.cs
        ├── ServiceBuilderTests.cs
        ├── ConfigMapBuilderTests.cs
        └── MemoryParsingTests.cs
```

