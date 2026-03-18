# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The **operator image** and **Helm chart** are versioned independently:
- Operator image tags: `operator/vMAJOR.MINOR.PATCH`
- Helm chart tags: `chart/vMAJOR.MINOR.PATCH`

---

## [Unreleased]

### Added
- Monorepo structure: `src/`, `charts/`, `manifests/`, `examples/`, `docs/`
- Independent release pipelines for the operator image (`operator/v*`) and Helm chart (`chart/v*`)
- Astro Starlight documentation site in `docs/`, using Bun as the package manager
- Multi-arch container image (`linux/amd64`, `linux/arm64`)
- Ubuntu Noble Chiseled runtime image (minimal, non-root, no shell)
- SLSA provenance attestation and SBOM generation on image releases
- OCI-based Helm chart publishing to `oci://ghcr.io/danihengeveld/charts/mc-operator`
- Dependabot configuration for GitHub Actions, NuGet, Docker, and npm/Bun
- `.editorconfig` and updated `.gitignore`
- `LICENSE` (MIT), `CHANGELOG.md`, `CONTRIBUTING.md`, `RELEASING.md`
- API group updated to `minecraft.dhv.sh/v1alpha1`

---

## Operator Image

### [1.0.0] — 2026-03-17

Initial production release of the mc-operator container image.

#### Added
- `MinecraftServer` CRD (`minecraft.dhv.sh/v1alpha1`)
- Reconciliation controller managing StatefulSet, Service, and ConfigMap
- Validating and mutating admission webhooks
- PVC lifecycle management with safe retain-by-default policy
- Leader election for multi-replica deployments
- Support for Vanilla, Paper, Spigot, and Bukkit server distributions
- Pause/resume via `spec.replicas: 0`
- Status reporting: phase, endpoint, PVC info, Kubernetes conditions

---

## Helm Chart

### [1.0.0] — 2026-03-17

Initial release of the mc-operator Helm chart.

#### Added
- Deployment, ClusterRole, ClusterRoleBinding, ServiceAccount, Namespace templates
- Webhook TLS volume mounts and cert-manager integration comments
- `values.yaml` with resource limits, log level, leader election, webhook configuration
- Published to `oci://ghcr.io/danihengeveld/charts/mc-operator`

---

[Unreleased]: https://github.com/danihengeveld/mc-operator/compare/operator/v1.0.0...HEAD
