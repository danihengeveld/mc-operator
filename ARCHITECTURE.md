# Architecture Notes: mc-operator v1

This document captures the key design decisions made for the v1 of the Minecraft Kubernetes Operator and the rationale behind each choice.

## Overview

`mc-operator` is a Kubernetes Operator built with .NET 10, maintained under the [dhv.sh](https://dhv.sh) umbrella and [KubeOps 10](https://github.com/buehler/dotnet-operator-sdk). It manages `MinecraftServer` and `MinecraftServerCluster` custom resources, which represent fully configured Minecraft server deployments and multi-server clusters with Velocity proxy support.

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

**Choice: `ReadWriteMany` access mode, retain by default (`deleteWithServer: false`)**

Minecraft world data is irreplaceable. By default:
- When a `MinecraftServer` is deleted, the PVC is **not** deleted
- The StatefulSet uses `PersistentVolumeClaimRetentionPolicy.WhenDeleted = "Retain"`
- A finalizer logs a warning about the retained PVC

Users must explicitly opt in to PVC deletion by setting `spec.storage.deleteWithServer: true`.

**`ReadWriteMany` access mode** is used so that the running server pod and a short-lived pre-pull Job can both mount the same PVC simultaneously during a version upgrade (see Decision 15). This requires a storage backend that supports `ReadWriteMany` — most production CSI drivers (NFS, Longhorn, Rook-Ceph, and many cloud block storage providers with the right StorageClass) support this. On vanilla single-node setups (e.g. k3s with local-path), `ReadWriteMany` is also supported since a single node can mount the same volume from multiple pods.

This decision prioritizes **safety over convenience**. The operational cost of a retained PVC is minor (some storage cost), but the cost of accidentally deleted world data is catastrophic and unrecoverable.

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
Minecraft servers are not horizontally scalable in the traditional sense. Multi-server setups require proxy servers (BungeeCord, Velocity). The `MinecraftServerCluster` CRD now manages multi-server topologies with Velocity proxy support, including automatic server registration and forwarding secret management. Full metric-driven auto-scaling (dynamic mode) is architected but pending metric collection implementation.

---

## Decision 11: MinecraftServerCluster CRD Design

**Choice: Separate CRD for clusters (`MinecraftServerCluster`)**

Rather than extending the `MinecraftServer` CRD with multi-server support, a separate `MinecraftServerCluster` CRD was created. This provides:

- **Clear separation of concerns**: Single-server management remains simple and focused
- **Distinct lifecycle phases**: Clusters have `Degraded` phase (some servers not ready) which doesn't apply to single servers
- **Composability**: The cluster controller creates `MinecraftServer` instances as child resources, reusing the same reconciliation logic
- **Independent validation**: Cluster validation rules (scaling constraints, proxy settings) don't complicate single-server webhooks

The cluster spec contains a `template` (same fields as MinecraftServerSpec minus replicas/service), `scaling` (mode and replica configuration), and `proxy` (Velocity settings).

---

## Decision 12: Velocity Proxy Deployment Strategy

**Choice: Deployment (not StatefulSet) for the Velocity proxy**

The Velocity proxy is **stateless** — it maintains only in-memory player sessions and routes traffic to backend servers. Using a Deployment provides:

- **Simple restarts**: No need for ordered startup/shutdown or stable identities
- **No PVC requirement**: The proxy stores no persistent data
- **Rolling updates**: Standard Deployment rollout strategy handles config changes
- **Future horizontal scaling**: Multiple proxy replicas are straightforward with a Deployment

The proxy image is `itzg/mc-proxy` (community standard), configured via `TYPE=VELOCITY` environment variable, matching the pattern used for backend servers with `itzg/minecraft-server`.

---

## Decision 13: Server Registration in Velocity

**Choice: ConfigMap-based `velocity.toml` generation**

The cluster controller generates `velocity.toml` dynamically and stores it in a ConfigMap. This ConfigMap is mounted into the proxy pod. When backend servers are added or removed:

1. The controller regenerates the `[servers]` section with current backend addresses (using Kubernetes internal DNS: `<server-name>.<namespace>.svc.cluster.local`)
2. The `[servers.try]` list is updated based on the `tryOrder` spec or ascending index order
3. The ConfigMap update triggers a proxy pod restart via annotation-based rollout

For **Modern** and **BungeeGuard** forwarding modes, a `forwarding.secret` file is generated with a random UUID and included in the same ConfigMap. Backend servers are automatically configured with `onlineMode: false` since the proxy handles authentication.

**Trade-off**: ConfigMap-based configuration requires a proxy restart to pick up changes. This was chosen over Velocity's plugin API because it requires no additional dependencies and works with the stock proxy image.

---

## Decision 14: Scaling Architecture

**Choice: Two scaling modes — Static and Dynamic**

- **Static mode**: Fixed replica count. Simple, predictable, suitable for most use cases.
- **Dynamic mode**: Auto-scale between `minReplicas` and `maxReplicas` based on a `ScalingPolicy`. The policy specifies a metric (currently `PlayerCount`) and a target utilization percentage.

The scaling infrastructure is fully architected:
- `ScalingSpec`, `ScalingPolicy`, and `ScalingMetric` types are defined
- Validation webhooks enforce constraints (min ≤ max, policy required for Dynamic)
- The controller reads the scaling config and provisions the correct number of servers

**What's pending**: Metric collection. In Dynamic mode, the controller currently provisions `minReplicas` servers. Once a metric collection mechanism is implemented (likely via RCON polling or a sidecar), the controller will scale between min and max based on actual player counts.

This "framework first" approach ensures the API and validation are stable before the full scaling loop is connected.

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
├── examples/                   # Example MinecraftServer and MinecraftServerCluster manifests
├── manifests/
│   ├── crd/                    # CustomResourceDefinition YAML (MinecraftServer + MinecraftServerCluster)
│   ├── rbac/                   # ClusterRole + ClusterRoleBinding
│   └── operator/               # Deployment, Service, webhooks (Kustomize)
└── src/
    ├── McOperator/             # .NET 10 operator application
    │   ├── Builders/           # Resource builders (StatefulSet, Service, ConfigMap, VelocityProxy, PrePullJob)
    │   ├── Controllers/        # Reconciliation controllers (MinecraftServer, MinecraftServerCluster)
    │   ├── Entities/           # CRD entity types (spec, status for both resources)
    │   ├── Extensions/         # Extension methods
    │   ├── Finalizers/         # PVC cleanup and cluster finalizers
    │   ├── Webhooks/           # Validating/mutating webhooks for both resources
    │   └── Program.cs          # Entry point and DI registration
    └── McOperator.Tests/       # TUnit unit tests
        ├── ValidationWebhookTests.cs
        ├── ClusterValidationWebhookTests.cs
        ├── StatefulSetBuilderTests.cs
        ├── PrePullJobBuilderTests.cs
        ├── VelocityProxyBuilderTests.cs
        ├── ServiceBuilderTests.cs
        ├── ConfigMapBuilderTests.cs
        ├── ClusterExtensionsTests.cs
        └── MemoryParsingTests.cs
```

---

## Decision 15: Zero-Downtime Version Upgrades via Pre-Pull Jobs

**Choice: Short-lived Kubernetes Job to pre-pull image and pre-download server jar before rolling update**

When a `MinecraftServer` version or image is changed, the operator creates a one-shot `batch/v1` Job (`<server-name>-prepull`) **before** applying the StatefulSet rolling update. This keeps server downtime to the absolute minimum — only the Minecraft process restart time, not the OCI image download or server jar fetch.

**Why this matters:** The default itzg/minecraft-server OCI image is ~700 MB compressed. The Minecraft server jar (e.g. vanilla 1.21.0) is ~50 MB. Without pre-pull, both downloads happen after the old pod is terminated, adding significant latency to every upgrade.

**Two modes depending on server configuration:**

| Mode | Condition | What the Job does |
|------|-----------|-------------------|
| Jar-download | Default itzg image + `storage.enabled = true` | Mounts the data PVC (`ReadWriteMany`), runs the itzg `/start` script with a fake `java` stub so startup scripts download the server jar to the PVC, then exits 0 |
| OCI-only | Custom `spec.image` or `storage.enabled = false` | Runs `sh -c "exit 0"` — forces the OCI image layers to be cached on the node, no jar download |

**Fake JVM stub technique (jar-download mode):** A tiny shell script (`#!/bin/sh\nexit 0`) is written to `/tmp/fj/java` and placed ahead of the real JVM in both `$PATH` and `$JAVA_CMD`. The itzg startup scripts resolve the Minecraft version and download the server jar to the mounted `/data` PVC exactly as they would in production. When they eventually try to launch the JVM, the stub exits 0, completing the Job cleanly without ever starting a Minecraft process. `SKIP_SERVER_PROPERTIES=true` prevents the startup scripts from overwriting `server.properties` that the live server is actively using.

**Pre-pull is skipped when:**
- No `status.currentImage` (fresh deployment — no existing version to compare)
- Desired image matches `status.currentImage` (no version change)
- `spec.replicas == 0` (server is paused)

**Failure handling:** If the pre-pull Job fails (bad image ref, registry auth issue, network error), the upgrade proceeds anyway. Pre-pull failure is a warning, not a blocker — the server pod will still start, it just won't benefit from the pre-pull optimisation.

**RBAC additions:** The operator requires `batch/jobs: *` and `pods: get` cluster-level permissions to create pre-pull Jobs and read the node assignment of the current server pod.

