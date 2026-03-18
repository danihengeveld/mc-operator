# mc-operator

A production-grade Kubernetes Operator for managing Minecraft server deployments, built with .NET 10 and [KubeOps](https://github.com/buehler/dotnet-operator-sdk).

**API group**: `minecraft.dhv.sh` · **Docs**: [mc-operator.dhv.sh](https://mc-operator.dhv.sh) · **Maintained by**: [dani.hengeveld.dev](https://dani.hengeveld.dev)

---

## Repository layout

```
mc-operator/
├── .github/
│   ├── dependabot.yml          # Automated dependency updates
│   └── workflows/
│       ├── ci.yml              # Build + test + docs on push/PR
│       ├── release-image.yml   # Publish image on operator/v* tags
│       └── release-chart.yml   # Publish Helm chart on chart/v* tags
├── charts/
│   └── mc-operator/            # Helm chart (OCI-published to GHCR)
├── docs/                       # Astro Starlight site (Bun)
│                               # Deployed to mc-operator.dhv.sh
├── examples/                   # Example MinecraftServer manifests
├── manifests/
│   ├── crd/                    # CustomResourceDefinition YAML
│   ├── rbac/                   # ClusterRole + ClusterRoleBinding
│   └── operator/               # Deployment, Service, webhooks (Kustomize)
└── src/
    ├── McOperator/             # .NET 10 operator application
    └── McOperator.Tests/       # TUnit unit tests
```

## Quick install

```bash
# Install the CRD
kubectl apply -f https://raw.githubusercontent.com/danihengeveld/mc-operator/main/manifests/crd/minecraftservers.yaml

# Install the operator via Helm (OCI chart)
helm install mc-operator oci://ghcr.io/danihengeveld/charts/mc-operator \
  --version 1.0.0 \
  --namespace mc-operator-system \
  --create-namespace
```

Then deploy a server:

```yaml
apiVersion: minecraft.dhv.sh/v1alpha1
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

## Container image

```
ghcr.io/danihengeveld/mc-operator:<version>
```

Built on [Ubuntu Noble Chiseled](https://ubuntu.com/blog/ubuntu-chiseled-and-dotnet) — minimal, non-root, no shell. Published for `linux/amd64` and `linux/arm64`.

## Releases

The operator image and Helm chart are **released independently**:

| Artefact | Tag | Example |
|----------|-----|---------|
| Operator image | `operator/vX.Y.Z` | `operator/v1.0.0` |
| Helm chart | `chart/vX.Y.Z` | `chart/v1.0.0` |

See [RELEASING.md](RELEASING.md) for the full release procedure and [CHANGELOG.md](CHANGELOG.md) for history.

## Documentation

Full documentation at **[mc-operator.dhv.sh](https://mc-operator.dhv.sh)**, including:

- [Quickstart](https://mc-operator.dhv.sh/getting-started/quickstart/)
- [CRD Reference](https://mc-operator.dhv.sh/reference/crd/)
- [Configuration Guide](https://mc-operator.dhv.sh/reference/configuration/)
- [Architecture](https://mc-operator.dhv.sh/guides/architecture/)
- [Running in Production](https://mc-operator.dhv.sh/guides/production/)
- [Development Guide](https://mc-operator.dhv.sh/development/guide/)

## Development

```bash
# Run tests
dotnet run --project src/McOperator.Tests/

# Build
dotnet build McOperator.slnx

# Lint the Helm chart
helm lint charts/mc-operator --strict

# Docs (requires Bun)
cd docs && bun install && bun run dev
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for full contributing guidelines.

## License

MIT — see [LICENSE](LICENSE).

