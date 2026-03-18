# Releasing mc-operator

The operator image and Helm chart are **versioned and released independently**. They each have their own tag namespace and release workflow.

| Artefact | Tag prefix | Example tag |
|----------|-----------|-------------|
| Operator image | `operator/v` | `operator/v1.2.3` |
| Helm chart | `chart/v` | `chart/v1.2.3` |

This separation means a chart bug-fix does not require a new operator image build, and an operator patch release does not force a chart version bump.

---

## Versioning policy

Both artefacts follow [Semantic Versioning](https://semver.org/):

| Bump | When |
|------|------|
| **PATCH** | Bug fixes, documentation fixes, no behaviour changes |
| **MINOR** | New backwards-compatible features |
| **MAJOR** | Breaking changes (CRD schema changes, API group changes, incompatible spec evolution) |

### When to bump the chart's `appVersion`

`Chart.yaml` carries an `appVersion` field that records the **minimum compatible operator image version** the chart targets. Update `appVersion` when the chart templates require a behaviour or API that was introduced in a newer operator release.

The chart `version` and `appVersion` evolve independently.

---

## Pre-release checklist

Before tagging either artefact:

1. [ ] All changes merged to `main` with CI passing (build, tests, helm lint, docs build)
2. [ ] For an operator release: verify that `charts/mc-operator/Chart.yaml` `appVersion` is correct
3. [ ] For a chart release: verify that `charts/mc-operator/values.yaml` `image.tag` default is appropriate

---

## Releasing the operator image

The image release pipeline is triggered by pushing a tag that matches `operator/v*.*.*`.

```bash
# 1. Ensure you are on main and CI is green
git checkout main && git pull

# 2. Create and push the tag
git tag -a operator/v1.2.3 -m "Operator release 1.2.3"
git push origin main operator/v1.2.3
```

GitHub Actions (`release-image.yml`) will:
- Build the multi-arch image (`linux/amd64`, `linux/arm64`)
- Push to `ghcr.io/danihengeveld/mc-operator` with tags:
  - `1.2.3` — exact version (pin this in production)
  - `1.2` — minor channel
  - `1` — major channel
  - `sha-<short>` — commit-level traceability
- Generate an SBOM and attach SLSA provenance
- Create a GitHub Release under the `operator/v1.2.3` tag

### Post-release

Verify the image is available:

```bash
docker pull ghcr.io/danihengeveld/mc-operator:1.2.3
```

Verify provenance:

```bash
gh attestation verify \
  oci://ghcr.io/danihengeveld/mc-operator:1.2.3 \
  --repo danihengeveld/mc-operator
```

---

## Releasing the Helm chart

The chart release pipeline is triggered by pushing a tag that matches `chart/v*.*.*`.

```bash
# 1. Ensure you are on main and CI is green
git checkout main && git pull

# 2. Update charts/mc-operator/Chart.yaml appVersion if needed

# 3. Commit any Chart.yaml appVersion change
git add charts/mc-operator/Chart.yaml
git commit -m "chore: release chart v1.2.3"

# 4. Create and push the tag
git tag -a chart/v1.2.3 -m "Helm chart release 1.2.3"
git push origin main chart/v1.2.3
```

GitHub Actions (`release-chart.yml`) will:
- Patch `Chart.yaml` with the release version (from the tag)
- Lint the chart
- Package it
- Push to `oci://ghcr.io/danihengeveld/charts/mc-operator` with the version tag
- Create a GitHub Release under the `chart/v1.2.3` tag with the `.tgz` attached

### Post-release

Verify the chart is available:

```bash
helm show chart oci://ghcr.io/danihengeveld/charts/mc-operator --version 1.2.3
```

---

## Releasing both at the same time

When a feature requires both an operator change and a chart change, release the operator first, then the chart:

```bash
# 1. Tag the operator image release
git tag -a operator/v1.2.0 -m "Operator release 1.2.0"
git push origin operator/v1.2.0

# 2. Wait for the image workflow to complete

# 3. Update Chart.yaml appVersion to "1.2.0" if the chart needs the new operator
# 4. Tag the chart release
git tag -a chart/v1.2.0 -m "Helm chart release 1.2.0"
git push origin chart/v1.2.0
```

---

## Rollback

If a release is found to be defective:

- **Image**: push a new `operator/vX.Y.Z+1` patch release. The previous image remains in GHCR and existing deployments are not affected unless they upgrade.
- **Chart**: push a new `chart/vX.Y.Z+1` patch release.
- **Do not delete tags** — this breaks provenance attestation and any tooling that references the digest.
