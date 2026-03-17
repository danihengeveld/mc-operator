---
title: Release Process
description: How mc-operator is versioned, built, and released — with independent image and chart versioning.
sidebar:
  order: 3
---

import { Aside } from '@astrojs/starlight/components';

## Independent versioning

The **operator image** and **Helm chart** are versioned and released independently. They each have their own tag namespace:

| Artefact | Tag pattern | Example |
|----------|------------|---------|
| Container image | `operator/vMAJOR.MINOR.PATCH` | `operator/v1.2.3` |
| Helm chart | `chart/vMAJOR.MINOR.PATCH` | `chart/v1.2.3` |

This means a chart bug-fix does not trigger a new image build, and an operator patch does not force a chart version bump.

## Versioning policy

Both artefacts follow [Semantic Versioning](https://semver.org/):

| Bump | When |
|------|------|
| **PATCH** | Bug fixes, no behaviour changes |
| **MINOR** | New backwards-compatible features |
| **MAJOR** | Breaking changes (CRD schema changes, API group changes) |

The Helm chart carries an `appVersion` in `Chart.yaml` that records the **minimum compatible operator version** the chart targets. Chart `version` and `appVersion` evolve independently.

## Release artefacts

### Operator image releases (`operator/v*`)

Triggered by pushing a tag matching `operator/v*.*.*`. Produces:

| Artefact | Location |
|----------|----------|
| Container image | `ghcr.io/danihengeveld/mc-operator:<version>` |
| SBOM + SLSA provenance | Attached to image in GHCR |
| GitHub Release | Under the `operator/v*` tag |

Image tags produced for `operator/v1.2.3`:

| Tag | Purpose |
|-----|---------|
| `1.2.3` | Exact version — pin this in production |
| `1.2` | Stable minor channel |
| `1` | Stable major channel |
| `sha-<short>` | Commit traceability |

### Helm chart releases (`chart/v*`)

Triggered by pushing a tag matching `chart/v*.*.*`. Produces:

| Artefact | Location |
|----------|----------|
| OCI chart | `oci://ghcr.io/danihengeveld/charts/mc-operator:<version>` |
| Chart tarball (`.tgz`) | Attached to the GitHub Release |
| GitHub Release | Under the `chart/v*` tag |

## How to create a release

See [RELEASING.md](https://github.com/danihengeveld/mc-operator/blob/main/RELEASING.md) in the repository root for the full step-by-step procedure, including:

- Pre-release checklist
- How to tag and push each artefact
- Post-release verification commands
- Rollback guidance

Quick reference:

```bash
# Operator image release
git tag -a operator/v1.2.3 -m "Operator release 1.2.3"
git push origin operator/v1.2.3

# Helm chart release
git tag -a chart/v1.2.3 -m "Helm chart release 1.2.3"
git push origin chart/v1.2.3
```

## Container image signing and provenance

The release pipeline generates:
- **SBOM** (Software Bill of Materials) attached as an OCI artifact
- **SLSA provenance** via `actions/attest-build-provenance`

Verify attestations:

```bash
gh attestation verify \
  oci://ghcr.io/danihengeveld/mc-operator:1.2.3 \
  --repo danihengeveld/mc-operator
```

## Changelog

All user-visible changes are documented in [CHANGELOG.md](https://github.com/danihengeveld/mc-operator/blob/main/CHANGELOG.md), following the [Keep a Changelog](https://keepachangelog.com/) format. Update the changelog before every release.

