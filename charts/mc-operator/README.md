# mc-operator Helm Chart

A Helm chart for deploying **mc-operator** — a production-grade Kubernetes Operator for managing Minecraft server deployments, built with .NET 10 and [KubeOps](https://github.com/buehler/dotnet-operator-sdk).

**API group**: `mc-operator.dhv.sh` · **Docs**: [mc-operator.dhv.sh](https://mc-operator.dhv.sh) · **Source**: [github.com/danihengeveld/mc-operator](https://github.com/danihengeveld/mc-operator)

---

## Overview

mc-operator manages the full lifecycle of Minecraft servers as first-class Kubernetes resources. Once installed, you can deploy and manage Minecraft servers using a simple `MinecraftServer` custom resource.

**Supported server distributions**: Vanilla, Paper, Spigot, CraftBukkit

For each `MinecraftServer` resource the operator creates and manages:
- A **StatefulSet** for the Minecraft server pod
- A **Service** exposing the game port (25565) and RCON port (25575)
- A **PersistentVolumeClaim** for world data and server configuration
- A **ConfigMap** containing the derived `server.properties`

---

## Prerequisites

| Requirement | Version |
|-------------|---------|
| Kubernetes  | 1.25+   |
| Helm        | 3.14+ (OCI chart support) |
| kubectl     | Matching your cluster version |

---

## Installing the Chart

### 1. Install the CRD

The `MinecraftServer` CRD must be installed before the operator:

```bash
kubectl apply -f https://raw.githubusercontent.com/danihengeveld/mc-operator/main/manifests/crd/minecraftservers.yaml
```

> **Note**: Always install the CRD version that matches your operator version.

### 2. Install the operator

The chart is published as an OCI artifact to GHCR:

```bash
helm install mc-operator oci://ghcr.io/danihengeveld/charts/mc-operator \
  --version <version> \
  --namespace mc-operator-system \
  --create-namespace
```

### 3. Verify the installation

```bash
# Operator pod should be Running
kubectl get pods -n mc-operator-system

# CRD should be established
kubectl get crd minecraftservers.mc-operator.dhv.sh
```

---

## Deploying a Minecraft Server

Once the operator is running, create a `MinecraftServer` resource:

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
    version: "1.21.4"
  jvm:
    initialMemory: "2G"
    maxMemory: "4G"
  storage:
    size: "20Gi"
```

```bash
kubectl apply -f server.yaml
kubectl get minecraftservers -n minecraft
```

---

## Configuration

The following table lists all configurable values with their defaults.

### Image

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `image.repository` | string | `ghcr.io/danihengeveld/mc-operator` | Operator container image repository |
| `image.tag` | string | `""` | Image tag. Defaults to the chart `appVersion` |
| `image.pullPolicy` | string | `IfNotPresent` | Image pull policy |
| `imagePullSecrets` | list | `[]` | List of image pull secret names |

### Deployment

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `replicaCount` | integer | `1` | Number of operator replicas. Use `1` unless enabling leader election with multiple replicas |
| `nameOverride` | string | `""` | Override the chart name |
| `fullnameOverride` | string | `""` | Override the fully qualified app name |

### Resources

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `resources.requests.cpu` | string | `100m` | CPU request for the operator pod |
| `resources.requests.memory` | string | `64Mi` | Memory request for the operator pod |
| `resources.limits.cpu` | string | `200m` | CPU limit for the operator pod |
| `resources.limits.memory` | string | `128Mi` | Memory limit for the operator pod |

### Scheduling

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `nodeSelector` | object | `{}` | Node selector for the operator pod |
| `tolerations` | list | `[]` | Tolerations for the operator pod |
| `affinity` | object | `{}` | Affinity rules for the operator pod |

### Service Account

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `serviceAccount.create` | bool | `true` | Create a dedicated ServiceAccount |
| `serviceAccount.name` | string | `""` | ServiceAccount name. Auto-generated from the release name if `create` is true and name is empty |
| `serviceAccount.annotations` | object | `{}` | Annotations to add to the ServiceAccount (e.g. for IRSA) |

### Namespace

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `namespace.create` | bool | `true` | Create the operator namespace |
| `namespace.name` | string | `mc-operator-system` | Namespace where the operator is deployed |

### Webhooks

The operator exposes validating and mutating admission webhooks that enforce constraints and apply defaults to `MinecraftServer` resources. Webhooks are always enabled and TLS certificates are automatically generated by the Helm chart.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `webhooks.port` | integer | `5001` | HTTPS port for the webhook server |

### Leader Election

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `leaderElection.enabled` | bool | `true` | Enable leader election for high-availability multi-replica deployments |

### Logging

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `logLevel` | string | `Information` | Log verbosity level. One of `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` |

### Metrics

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `metrics.enabled` | bool | `false` | Enable the metrics endpoint (future use) |
| `metrics.port` | integer | `8080` | Port for the metrics endpoint |

---

## Webhook TLS

The Kubernetes API server must reach the operator over HTTPS for webhooks to function. The Helm chart automatically generates a self-signed CA and TLS certificate for the webhook service during installation. The generated `caBundle` is injected into the `ValidatingWebhookConfiguration` and `MutatingWebhookConfiguration` resources automatically.

Certificates are persisted across `helm upgrade` operations — the chart reuses existing certificates if the webhook Secret already exists in the cluster.

---

## Upgrading

```bash
helm upgrade mc-operator oci://ghcr.io/danihengeveld/charts/mc-operator \
  --version <new-version> \
  --namespace mc-operator-system
```

After upgrading the chart, also update the CRD if the new version includes CRD changes:

```bash
kubectl apply -f https://raw.githubusercontent.com/danihengeveld/mc-operator/main/manifests/crd/minecraftservers.yaml
```

---

## Uninstalling

```bash
helm uninstall mc-operator --namespace mc-operator-system
```

> **Warning**: Uninstalling the operator does **not** delete any `MinecraftServer` resources or the data held in their PersistentVolumeClaims. Clean those up manually if needed.

To also remove the CRD (this will delete all MinecraftServer resources):

```bash
kubectl delete crd minecraftservers.mc-operator.dhv.sh
```

---

## RBAC

The chart creates a `ClusterRole` granting the operator permissions to manage:

| Resource | API Group | Verbs |
|----------|-----------|-------|
| `statefulsets` | `apps` | `*` |
| `services`, `configmaps`, `persistentvolumeclaims` | `""` | `*` |
| `events` | `""` | `get`, `list`, `create`, `update`, `patch` |
| `leases` | `coordination.k8s.io` | `*` |
| `minecraftservers` | `mc-operator.dhv.sh` | `*` |
| `minecraftservers/status` | `mc-operator.dhv.sh` | `get`, `update`, `patch` |

---

## Example Values

### Minimal

```yaml
logLevel: Warning
```

### Production

```yaml
replicaCount: 1
resources:
  requests:
    cpu: 100m
    memory: 64Mi
  limits:
    cpu: 500m
    memory: 256Mi
leaderElection:
  enabled: true
logLevel: Information
```

### Custom image tag

```yaml
image:
  repository: ghcr.io/danihengeveld/mc-operator
  tag: "1.2.3"
  pullPolicy: Always
```

---

## Documentation

Full documentation is available at **[mc-operator.dhv.sh](https://mc-operator.dhv.sh)**:

- [Getting Started](https://mc-operator.dhv.sh/getting-started/introduction/)
- [Installation Guide](https://mc-operator.dhv.sh/getting-started/installation/)
- [CRD Reference](https://mc-operator.dhv.sh/reference/crd/)
- [Configuration Guide](https://mc-operator.dhv.sh/reference/configuration/)
- [Architecture](https://mc-operator.dhv.sh/guides/architecture/)
- [Running in Production](https://mc-operator.dhv.sh/guides/production/)
- [Troubleshooting](https://mc-operator.dhv.sh/guides/troubleshooting/)

---

## License

MIT — see [LICENSE](https://github.com/danihengeveld/mc-operator/blob/main/LICENSE).
