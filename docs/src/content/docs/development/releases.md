---
title: Release Process
description: How mc-operator is versioned, built, and released.
sidebar:
  order: 3
---

## Versioning

mc-operator uses [Semantic Versioning](https://semver.org/):

- `MAJOR.MINOR.PATCH`
- **PATCH**: bug fixes, no breaking changes, no new features
- **MINOR**: new features, backwards-compatible changes
- **MAJOR**: breaking changes (CRD schema changes that require migration, API group changes, incompatible behaviour)

The API is currently at `v1alpha1`. Breaking changes to the CRD schema may occur in minor releases until the API graduates to `v1beta1`.

## Release artefacts

Each tagged release produces:

| Artefact | Location | Description |
|----------|----------|-------------|
| Container image | `ghcr.io/danihengeveld/mc-operator:<version>` | Multi-arch (amd64, arm64) |
| Helm chart | `oci://ghcr.io/danihengeveld/charts/mc-operator:<version>` | OCI-based Helm chart |
| GitHub Release | [Releases page](https://github.com/danihengeveld/mc-operator/releases) | Release notes + chart tarball |

## How to create a release

1. Ensure all changes are merged to `main` and CI passes.

2. Create and push a version tag:

   ```bash
   git tag -a v1.1.0 -m "Release v1.1.0"
   git push origin v1.1.0
   ```

3. The tag triggers two workflows automatically:
   - **`release-image.yml`**: builds and pushes the container image to GHCR with semver, major, minor, and SHA tags
   - **`release-chart.yml`**: packages and pushes the Helm chart to GHCR OCI and creates a GitHub Release with release notes and the chart tarball

4. Review the GitHub Release page and edit the notes if needed.

## Image tags

The release pipeline produces the following image tags for a `v1.2.3` release:

| Tag | Example | Purpose |
|-----|---------|---------|
| Full semver | `1.2.3` | Exact version pin |
| Minor | `1.2` | Stable minor channel |
| Major | `1` | Stable major channel |
| SHA | `sha-a1b2c3d` | Traceability to exact commit |

The `latest` tag is **not** published to avoid accidental unintentional upgrades in production.

## Container image signing and provenance

The release pipeline generates:
- **SBOM** (Software Bill of Materials) attached as an OCI artifact
- **SLSA provenance** attestation via `actions/attest-build-provenance`

Verify attestations with:

```bash
gh attestation verify \
  oci://ghcr.io/danihengeveld/mc-operator:1.2.3 \
  --repo danihengeveld/mc-operator
```

## Helm chart release

Charts are published to GHCR as OCI artifacts. Install any version:

```bash
helm install mc-operator oci://ghcr.io/danihengeveld/charts/mc-operator \
  --version 1.2.3 \
  --namespace mc-operator-system \
  --create-namespace
```

The chart `version` and `appVersion` are automatically set to match the git tag during the release workflow.
