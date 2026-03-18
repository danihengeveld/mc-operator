# Integration Tests

Integration tests for mc-operator using [TUnit](https://github.com/thomhurst/TUnit) and [Testcontainers](https://dotnet.testcontainers.org/).

## How it works

The tests spin up a real [k3s](https://k3s.io/) Kubernetes cluster inside a Docker container (via Testcontainers), apply the MinecraftServer CRD, and run the operator in-process connected to that cluster.

### Architecture

```
┌─────────────────────────────────────────┐
│  Test Process                           │
│                                         │
│  ┌──────────────┐  ┌─────────────────┐  │
│  │  TUnit Tests  │  │  Operator       │  │
│  │              │──▶│  (in-process)   │  │
│  └──────┬───────┘  └────────┬────────┘  │
│         │                   │           │
│         └──────┬────────────┘           │
│                ▼                        │
│       ┌────────────────┐                │
│       │  k8s client    │                │
│       └───────┬────────┘                │
└───────────────┼─────────────────────────┘
                │ kubeconfig
                ▼
       ┌────────────────────┐
       │  k3s container     │
       │  (Testcontainers)  │
       └────────────────────┘
```

**Why in-process?** Running the operator in-process (rather than deploying it as a container in k3s) is:
- **Fastest**: No Docker image build or container registry needed
- **Most reliable**: Avoids Docker-in-Docker complexity
- **Easy to debug**: Operator logs appear directly in test output

The tradeoff is that webhook admission validation isn't tested end-to-end (webhooks need the operator to be network-reachable from the API server). This is acceptable because webhook logic is already well-covered by unit tests (`ValidationWebhookTests`).

## Running locally

### Prerequisites

- Docker (running)
- .NET 10 SDK

### Run the tests

```bash
dotnet run --project src/McOperator.IntegrationTests/ -c Release
```

Or to build and run separately:

```bash
dotnet build src/McOperator.IntegrationTests/ -c Release
dotnet run --project src/McOperator.IntegrationTests/ -c Release --no-build
```

### First run

The first run will pull the k3s Docker image (~200MB), which may take a few minutes. Subsequent runs reuse the cached image.

## Test structure

```
McOperator.IntegrationTests/
├── Infrastructure/
│   ├── K3sFixture.cs          # Assembly-wide k3s cluster + operator lifecycle
│   └── KubernetesHelper.cs    # Helpers: create namespaces, wait for resources, etc.
├── Manifests/
│   └── valid-minecraft-server.yaml   # Reference manifest (used as documentation)
├── Tests/
│   ├── HappyPathTests.cs      # Create valid CR → verify child resources
│   ├── UpdateFlowTests.cs     # Modify CR → verify reconciliation updates
│   ├── ValidationTests.cs     # Invalid CRs rejected by CRD schema
│   └── StatusConditionTests.cs # Status/conditions reflect actual state
└── README.md
```

## Test isolation

Each test creates its own Kubernetes namespace with a unique name. This ensures tests don't interfere with each other, even when running in parallel. Namespaces are cleaned up in a `finally` block after each test.

The k3s cluster itself is shared across all tests for performance (started once per assembly via `SharedType.PerAssembly`).

## Known limitations

- **Webhooks not tested end-to-end**: As noted above, admission webhooks require network connectivity between the API server and the operator. This would require deploying the operator as a container inside k3s with TLS certificates configured, which adds significant complexity. Webhook logic is well-covered by unit tests.
- **Pod readiness**: The Minecraft server container (`itzg/minecraft-server`) is not actually pulled/started in most tests since we're primarily testing the operator's reconciliation logic (creating StatefulSets, Services, etc.), not the game server itself. The pod will be in a `Pending` or `ImagePullBackOff` state, which is expected.
- **Runtime**: The test suite takes ~1-2 minutes depending on Docker image cache state. The k3s container startup is the main bottleneck (~10-20 seconds).

## Extending

To add new test scenarios:

1. Create a new test class in `Tests/`
2. Add the fixture injection:
   ```csharp
   [ClassDataSource<K3sFixture>(Shared = SharedType.PerAssembly)]
   public required K3sFixture K3s { get; init; }
   ```
3. Use `KubernetesHelper` methods for namespace isolation and resource polling
4. Always clean up namespaces in a `finally` block
