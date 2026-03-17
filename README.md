# mc-operator

A production-ready Kubernetes Operator for managing Minecraft server deployments, built with .NET 10 and [KubeOps](https://github.com/buehler/dotnet-operator-sdk).

## Features

- **Multiple server distributions**: Vanilla, Paper, Spigot, Bukkit
- **Kubernetes-native**: CRDs, validating/mutating webhooks, status conditions, owner references, finalizers
- **Persistent storage**: PVC lifecycle management with safe defaults (data retained on delete)
- **Full server configuration**: All common `server.properties` settings exposed in the CRD spec
- **JVM tuning**: Control heap size and JVM flags per server
- **Service exposure**: ClusterIP, NodePort, LoadBalancer support
- **Server lifecycle**: Pause servers (replicas=0) without losing world data
- **Status reporting**: Phase, conditions, ready replicas, endpoint info, PVC status
- **Leader election**: Safe multi-replica operator deployments
- **Helm chart**: Clean, reproducible installation

## Supported Server Types

| Type    | Description |
|---------|-------------|
| Vanilla | Official Mojang server |
| Paper   | High-performance Paper fork (recommended for production) |
| Spigot  | Spigot server with plugin support |
| Bukkit  | CraftBukkit server |

All distributions are powered by the battle-tested [`itzg/minecraft-server`](https://github.com/itzg/docker-minecraft-server) Docker image, which handles version management and server.properties configuration via environment variables.

## Installation

### Prerequisites

- Kubernetes 1.25+
- `kubectl` configured for your cluster
- (For webhooks) A method to serve TLS to the Kubernetes API server (cert-manager or the built-in certificate mechanism)

### Install with Helm

```bash
# Apply the CRDs first
kubectl apply -f config/crd/minecraftservers.yaml

# Install the operator
helm install mc-operator deploy/helm/mc-operator \
  --namespace mc-operator-system \
  --create-namespace
```

### Install with kubectl (Kustomize)

```bash
kubectl apply -f config/crd/minecraftservers.yaml
kubectl apply -f config/rbac/
kubectl apply -f config/operator/
```

### Webhooks Setup

The operator uses validating and mutating admission webhooks. In production, you need to:

1. Generate TLS certificates for the webhook service (use [cert-manager](https://cert-manager.io/) in production)
2. The generated `config/operator/validators.yaml` and `config/operator/mutators.yaml` reference a `webhook-cert` and `webhook-ca` Secret

For local development, KubeOps can use [ngrok](https://ngrok.com/) as a development tunnel:
```bash
# In development mode, the operator registers webhooks pointing to your local machine
dotnet run --project src/McOperator/
```

## Quick Start

1. Create a namespace for your Minecraft servers:
```bash
kubectl create namespace minecraft
```

2. Deploy a Paper server:
```bash
kubectl apply -f config/examples/paper-server.yaml
```

3. Watch the server come up:
```bash
kubectl get minecraftservers -n minecraft -w
```

4. Check server status:
```bash
kubectl describe minecraftserver paper-survival -n minecraft
```

5. Connect to the server (using port-forward for ClusterIP):
```bash
kubectl port-forward svc/paper-survival 25565:25565 -n minecraft
# Then connect with Minecraft client to localhost:25565
```

## CRD Reference

### MinecraftServer Spec

```yaml
apiVersion: minecraft.operator.io/v1alpha1
kind: MinecraftServer
metadata:
  name: my-server
  namespace: minecraft
spec:
  # REQUIRED: Accept the Minecraft EULA
  acceptEula: true

  # Optional: Override the container image (uses itzg/minecraft-server:latest by default)
  # image: "my-custom-image:tag"

  # Optional: Number of replicas (0 = paused, 1 = running; Minecraft is a stateful singleton)
  replicas: 1

  server:
    type: Paper          # Vanilla | Paper | Spigot | Bukkit
    version: "1.20.4"   # Minecraft version, or "LATEST"

  properties:
    motd: "My Minecraft Server"
    difficulty: Normal       # Peaceful | Easy | Normal | Hard
    gameMode: Survival       # Survival | Creative | Adventure | Spectator
    maxPlayers: 20
    onlineMode: true         # Set false for offline/cracked mode
    whitelistEnabled: false
    whitelist: []            # Player usernames
    pvp: true
    hardcore: false
    spawnProtection: 16
    viewDistance: 10
    simulationDistance: 10
    levelName: world
    levelSeed: ""            # Empty = random seed
    levelType: DEFAULT       # DEFAULT | FLAT | LARGBIOMES | AMPLIFIED
    generateStructures: true
    allowNether: true
    serverPort: 25565
    ops: []                  # Operator player usernames
    additionalProperties: {} # Raw server.properties overrides

  jvm:
    initialMemory: "1G"    # -Xms
    maxMemory: "2G"        # -Xmx
    extraJvmArgs: []       # Additional JVM flags

  resources:
    cpuRequest: "500m"
    cpuLimit: "2"
    memoryRequest: "2Gi"
    memoryLimit: "2Gi"

  storage:
    enabled: true
    size: "10Gi"
    storageClassName: ""     # Empty = cluster default StorageClass
    deleteWithServer: false  # DANGER: set true to delete PVC on server deletion
    mountPath: /data

  service:
    type: ClusterIP          # ClusterIP | NodePort | LoadBalancer
    nodePort: null           # 30000-32767, only valid when type=NodePort
    annotations: {}          # Service annotations (e.g., for cloud LB)
```

### MinecraftServer Status

```yaml
status:
  phase: Running          # Pending | Provisioning | Running | Paused | Failed | Terminating
  message: "Server is running"
  observedGeneration: 3
  readyReplicas: 1
  currentImage: "itzg/minecraft-server:latest"
  currentVersion: "1.20.4"
  endpoint:
    host: "10.96.1.100"
    port: 25565
    connectionString: "10.96.1.100:25565"
  storage:
    name: "data-my-server-0"
    phase: "Bound"
    storageClassName: "standard"
    capacity: "10Gi"
  conditions:
  - type: Available
    status: "True"
    reason: ServerRunning
    message: "Server is running"
  - type: Progressing
    status: "False"
    reason: Idle
    message: "Server is running"
  - type: Degraded
    status: "False"
    reason: OK
    message: "No issues"
```

## Example Configurations

See [config/examples/](config/examples/) for complete example manifests:

- **[vanilla-server.yaml](config/examples/vanilla-server.yaml)** - Vanilla server with LoadBalancer
- **[paper-server.yaml](config/examples/paper-server.yaml)** - Paper server with NodePort and whitelist
- **[spigot-server.yaml](config/examples/spigot-server.yaml)** - Small private Spigot server
- **[bukkit-creative.yaml](config/examples/bukkit-creative.yaml)** - Bukkit creative server with flat world
- **[paused-server.yaml](config/examples/paused-server.yaml)** - How to pause a server (replicas=0)

## Development

### Prerequisites

- .NET 10 SDK
- Docker (for building container images)
- A Kubernetes cluster (local: kind, minikube, k3s, or Docker Desktop)
- `kubectl`

### Running Locally

```bash
# Clone and restore dependencies
git clone https://github.com/danihengeveld/mc-operator.git
cd mc-operator
dotnet restore

# Run tests
dotnet test src/McOperator.Tests/

# Run the operator locally against your cluster
# (uses the currently active kubeconfig)
dotnet run --project src/McOperator/
```

### Building the Docker Image

```bash
docker build -t ghcr.io/danihengeveld/mc-operator:dev .
```

### Regenerating CRDs and Operator Manifests

```bash
# Install the KubeOps CLI tool
dotnet tool restore

# Regenerate all manifests (CRDs, RBAC, Deployment, Webhooks)
dotnet kubeops generate operator "mc-operator" src/McOperator/McOperator.csproj \
  --out config/ \
  --docker-image "ghcr.io/danihengeveld/mc-operator" \
  --docker-image-tag "latest"
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~ValidationWebhookTests"
```

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed design decisions and rationale.

## Known Limitations

### v1 Scope

- **No plugin/modpack support**: Installing plugins or modpacks is not automated. Users can manage them by exec-ing into the pod or using init containers. This is intentional for v1 to keep scope manageable.
- **No RCON support**: RCON management (remote console) is not exposed via the operator. Use `kubectl exec` for server commands.
- **No backup automation**: Automated world backups are not included in v1. Consider tools like [Velero](https://velero.io/) for PVC snapshots.
- **Single-replica only**: Minecraft is not horizontally scalable. The `replicas` field is constrained to 0 or 1.
- **Storage size immutable**: After creation, storage size cannot be changed via the operator (standard PVC limitation). Resize the PVC directly if needed.
- **No auto-scaling**: Player-count-based auto-scaling is not implemented.

## Roadmap

Potential future features (not in v1):

- Plugin management (specify plugin JARs or repositories)
- Automatic backup to object storage (S3, GCS)
- RCON integration for server commands via CRD
- Multiple instances/proxy support (BungeeCord, Velocity)
- Server metrics scraping (Prometheus)
- Version auto-update support
- Bedrock server support

## License

MIT License - see [LICENSE](LICENSE) for details.
